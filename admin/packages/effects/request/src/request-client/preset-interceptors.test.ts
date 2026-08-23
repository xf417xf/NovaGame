import type { RequestClient } from './request-client';

import { describe, expect, it, vi } from 'vitest';

import { authenticateResponseInterceptor } from './preset-interceptors';

function createDeferred<T>() {
  let reject: (reason?: unknown) => void = () => {
    throw new Error('Deferred promise rejection handler is unavailable.');
  };
  let resolve: (value: PromiseLike<T> | T) => void = () => {
    throw new Error('Deferred promise resolution handler is unavailable.');
  };
  const promise = new Promise<T>((promiseResolve, promiseReject) => {
    reject = promiseReject;
    resolve = promiseResolve;
  });

  return { promise, reject, resolve };
}

function unauthorizedRequest(url: string) {
  return {
    config: {
      headers: {},
      url,
    },
    response: { status: 401 },
  };
}

describe('authenticateResponseInterceptor', () => {
  it('propagates a cancelled refresh to queued requests without reauthentication', async () => {
    const refresh = createDeferred<string>();
    const request = vi.fn();
    const doReAuthenticate = vi.fn(async () => {});
    const client = {
      isRefreshing: false,
      refreshTokenQueue: [],
      request,
    } as unknown as RequestClient;
    const interceptor = authenticateResponseInterceptor({
      client,
      doReAuthenticate,
      doRefreshToken: () => refresh.promise,
      enableRefreshToken: true,
      formatToken: (token) => (token ? `Bearer ${token}` : null),
    });
    const rejected = interceptor.rejected;
    if (!rejected) {
      throw new Error(
        'The authentication interceptor did not register rejection handling.',
      );
    }

    const primaryRequest = rejected(unauthorizedRequest('/primary'));
    const queuedRequest = rejected(unauthorizedRequest('/queued'));
    const cancellation = Object.assign(
      new Error('The session refresh was superseded.'),
      {
        __CANCEL__: true,
        code: 'ERR_CANCELED',
        name: 'CanceledError',
      },
    );

    refresh.reject(cancellation);

    await expect(primaryRequest).rejects.toBe(cancellation);
    await expect(queuedRequest).rejects.toBe(cancellation);
    expect(doReAuthenticate).not.toHaveBeenCalled();
    expect(request).not.toHaveBeenCalled();
    expect(client.refreshTokenQueue).toEqual([]);
    expect(client.isRefreshing).toBe(false);
  });

  it('does not strand a concurrent 401 while reauthentication is pending', async () => {
    const refresh = createDeferred<string>();
    const reauthentication = createDeferred<undefined>();
    const request = vi.fn();
    const doReAuthenticate = vi.fn(() => reauthentication.promise);
    const doRefreshToken = vi.fn(() => refresh.promise);
    const client = {
      isRefreshing: false,
      refreshTokenQueue: [],
      request,
    } as unknown as RequestClient;
    const interceptor = authenticateResponseInterceptor({
      client,
      doReAuthenticate,
      doRefreshToken,
      enableRefreshToken: true,
      formatToken: (token) => (token ? `Bearer ${token}` : null),
    });
    const rejected = interceptor.rejected;
    if (!rejected) {
      throw new Error(
        'The authentication interceptor did not register rejection handling.',
      );
    }
    const refreshFailure = new Error('The refresh request failed.');
    const concurrentError = unauthorizedRequest('/during-reauthentication');

    const primaryRequest = rejected(unauthorizedRequest('/primary'));
    const queuedRequest = rejected(unauthorizedRequest('/queued'));
    const queuedFailure = queuedRequest.catch((error) => error);
    refresh.reject(refreshFailure);

    await vi.waitFor(() => {
      expect(doReAuthenticate).toHaveBeenCalledOnce();
    });

    const concurrentRequest = rejected(concurrentError);
    expect(client.refreshTokenQueue).toEqual([]);

    reauthentication.resolve(undefined);

    await expect(primaryRequest).rejects.toBe(refreshFailure);
    expect(await queuedFailure).toBe(refreshFailure);
    await expect(concurrentRequest).rejects.toBe(concurrentError);
    expect(doRefreshToken).toHaveBeenCalledOnce();
    expect(doReAuthenticate).toHaveBeenCalledOnce();
    expect(request).not.toHaveBeenCalled();
    expect(client.refreshTokenQueue).toEqual([]);
    expect(client.isRefreshing).toBe(false);
  });

  it('settles a 401 that arrives as reauthentication completes', async () => {
    const refresh = createDeferred<string>();
    const reauthentication = createDeferred<undefined>();
    const retryResponse = { succeeded: true };
    const request = vi.fn().mockResolvedValue(retryResponse);
    const doReAuthenticate = vi.fn(() => reauthentication.promise);
    const doRefreshToken = vi
      .fn()
      .mockReturnValueOnce(refresh.promise)
      .mockResolvedValueOnce('replacement-access-token');
    const client = {
      isRefreshing: false,
      refreshTokenQueue: [],
      request,
    } as unknown as RequestClient;
    const interceptor = authenticateResponseInterceptor({
      client,
      doReAuthenticate,
      doRefreshToken,
      enableRefreshToken: true,
      formatToken: (token) => (token ? `Bearer ${token}` : null),
    });
    const rejected = interceptor.rejected;
    if (!rejected) {
      throw new Error(
        'The authentication interceptor did not register rejection handling.',
      );
    }
    const refreshFailure = new Error('The refresh request failed.');
    const completionError = unauthorizedRequest(
      '/at-reauthentication-complete',
    );

    const primaryRequest = rejected(unauthorizedRequest('/primary'));
    const queuedRequest = rejected(unauthorizedRequest('/queued'));
    const primaryFailure = primaryRequest.catch((error) => error);
    const queuedFailure = queuedRequest.catch((error) => error);
    refresh.reject(refreshFailure);

    await vi.waitFor(() => {
      expect(doReAuthenticate).toHaveBeenCalledOnce();
    });

    // Register after the failure path has its reauthentication continuation.
    const completionRequest = reauthentication.promise.then(() =>
      rejected(completionError),
    );
    reauthentication.resolve(undefined);

    expect(await primaryFailure).toBe(refreshFailure);
    expect(await queuedFailure).toBe(refreshFailure);
    await expect(completionRequest).resolves.toBe(retryResponse);
    expect(doRefreshToken).toHaveBeenCalledTimes(2);
    expect(doReAuthenticate).toHaveBeenCalledOnce();
    expect(request).toHaveBeenCalledTimes(1);
    expect(client.refreshTokenQueue).toEqual([]);
    expect(client.isRefreshing).toBe(false);
  });

  it('refreshes a later 401 after a concurrent reauthentication settles during a successful refresh', async () => {
    const refresh = createDeferred<string>();
    const reauthentication = createDeferred<undefined>();
    const retryResponse = { succeeded: true };
    const request = vi.fn().mockResolvedValue(retryResponse);
    const doReAuthenticate = vi.fn(() => reauthentication.promise);
    const doRefreshToken = vi
      .fn()
      .mockReturnValueOnce(refresh.promise)
      .mockResolvedValueOnce('replacement-access-token');
    const client = {
      isRefreshing: false,
      refreshTokenQueue: [],
      request,
    } as unknown as RequestClient;
    const interceptor = authenticateResponseInterceptor({
      client,
      doReAuthenticate,
      doRefreshToken,
      enableRefreshToken: true,
      formatToken: (token) => (token ? `Bearer ${token}` : null),
    });
    const rejected = interceptor.rejected;
    if (!rejected) {
      throw new Error(
        'The authentication interceptor did not register rejection handling.',
      );
    }

    const primaryRequest = rejected(unauthorizedRequest('/primary'));
    const retryError = {
      config: {
        __isRetryRequest: true,
        headers: {},
        url: '/retry',
      },
      response: { status: 401 },
    };
    const retryRequest = rejected(retryError);

    reauthentication.resolve(undefined);

    await expect(retryRequest).rejects.toBe(retryError);
    expect(client.isRefreshing).toBe(true);

    refresh.resolve('refreshed-access-token');

    await expect(primaryRequest).resolves.toBe(retryResponse);

    const freshRequest = rejected(unauthorizedRequest('/fresh'));

    await expect(freshRequest).resolves.toBe(retryResponse);
    expect(doRefreshToken).toHaveBeenCalledTimes(2);
    expect(doReAuthenticate).toHaveBeenCalledOnce();
    expect(request).toHaveBeenCalledTimes(2);
    expect(client.refreshTokenQueue).toEqual([]);
    expect(client.isRefreshing).toBe(false);
  });

  it('refreshes a later 401 after a concurrent reauthentication settles during a cancelled refresh', async () => {
    const refresh = createDeferred<string>();
    const reauthentication = createDeferred<undefined>();
    const retryResponse = { succeeded: true };
    const request = vi.fn().mockResolvedValue(retryResponse);
    const doReAuthenticate = vi.fn(() => reauthentication.promise);
    const doRefreshToken = vi
      .fn()
      .mockReturnValueOnce(refresh.promise)
      .mockResolvedValueOnce('replacement-access-token');
    const client = {
      isRefreshing: false,
      refreshTokenQueue: [],
      request,
    } as unknown as RequestClient;
    const interceptor = authenticateResponseInterceptor({
      client,
      doReAuthenticate,
      doRefreshToken,
      enableRefreshToken: true,
      formatToken: (token) => (token ? `Bearer ${token}` : null),
    });
    const rejected = interceptor.rejected;
    if (!rejected) {
      throw new Error(
        'The authentication interceptor did not register rejection handling.',
      );
    }

    const primaryRequest = rejected(unauthorizedRequest('/primary'));
    const retryError = {
      config: {
        __isRetryRequest: true,
        headers: {},
        url: '/retry',
      },
      response: { status: 401 },
    };
    const retryRequest = rejected(retryError);

    reauthentication.resolve(undefined);

    await expect(retryRequest).rejects.toBe(retryError);
    expect(client.isRefreshing).toBe(true);

    const cancellation = Object.assign(
      new Error('The session refresh was superseded.'),
      {
        __CANCEL__: true,
        code: 'ERR_CANCELED',
        name: 'CanceledError',
      },
    );
    refresh.reject(cancellation);

    await expect(primaryRequest).rejects.toBe(cancellation);

    const freshRequest = rejected(unauthorizedRequest('/fresh'));

    await expect(freshRequest).resolves.toBe(retryResponse);
    expect(doRefreshToken).toHaveBeenCalledTimes(2);
    expect(doReAuthenticate).toHaveBeenCalledOnce();
    expect(request).toHaveBeenCalledOnce();
    expect(client.refreshTokenQueue).toEqual([]);
    expect(client.isRefreshing).toBe(false);
  });
});
