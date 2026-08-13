import type {
  AxiosAdapter,
  AxiosResponse,
  InternalAxiosRequestConfig,
} from '@vben/request';

import { AxiosError, AxiosHeaders } from '@vben/request';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const state = vi.hoisted(() => {
  const accessStore = {
    accessToken: null as null | string,
    setAccessToken: vi.fn((token: null | string) => {
      accessStore.accessToken = token;
    }),
    setRefreshToken: vi.fn(),
  };

  return {
    accessStore,
    expireSession: vi.fn(),
  };
});

vi.mock('@vben/hooks', () => ({
  useAppConfig: () => ({ apiURL: '/api' }),
}));

vi.mock('@vben/preferences', () => ({
  preferences: { app: { locale: 'en-US' } },
}));

vi.mock('@vben/stores', () => ({
  useAccessStore: () => state.accessStore,
}));

vi.mock('#/store', () => ({
  useAuthStore: () => ({ expireSession: state.expireSession }),
}));

import {
  publicPost,
  publicPostVoid,
  publicRequestClient,
  refreshAccessToken,
  requestClient,
} from './request';

interface Deferred<T> {
  promise: Promise<T>;
  reject: (reason?: unknown) => void;
  resolve: (value: T) => void;
}

function createDeferred<T>(): Deferred<T> {
  let reject!: (reason?: unknown) => void;
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    reject = rejectPromise;
    resolve = resolvePromise;
  });
  return { promise, reject, resolve };
}

function createResponse<T>(
  config: InternalAxiosRequestConfig,
  status: number,
  data: T,
): AxiosResponse<T> {
  return {
    config,
    data,
    headers: new AxiosHeaders(),
    status,
    statusText: String(status),
  };
}

function createHttpError(
  config: InternalAxiosRequestConfig,
  status: number,
  data: unknown,
): AxiosError {
  return new AxiosError(
    `Request failed with status code ${status}`,
    AxiosError.ERR_BAD_REQUEST,
    config,
    undefined,
    createResponse(config, status, data),
  );
}

function successEnvelope<T>(data: T) {
  return { data, error: null, succeeded: true as const };
}

function failureEnvelope(code: string, message: string) {
  return {
    error: { code, details: null, message },
    succeeded: false as const,
  };
}

function readAuthorization(config: InternalAxiosRequestConfig): null | string {
  const value = AxiosHeaders.from(config.headers).get('Authorization');
  return typeof value === 'string' ? value : null;
}

const originalPublicAdapter = publicRequestClient.instance.defaults.adapter;
const originalProtectedAdapter = requestClient.instance.defaults.adapter;

describe('administrator request policy', () => {
  beforeEach(() => {
    state.accessStore.accessToken = null;
    state.accessStore.setAccessToken.mockClear();
    state.accessStore.setRefreshToken.mockClear();
    state.expireSession.mockClear();
  });

  afterEach(() => {
    publicRequestClient.instance.defaults.adapter = originalPublicAdapter;
    requestClient.instance.defaults.adapter = originalProtectedAdapter;
  });

  it('keeps cookie credentials enabled for public and protected requests', () => {
    expect(publicRequestClient.instance.defaults).toMatchObject({
      baseURL: '/api',
      withCredentials: true,
    });
    expect(requestClient.instance.defaults).toMatchObject({
      baseURL: '/api',
      withCredentials: true,
    });
  });

  it('posts public authentication envelopes with cookie credentials', async () => {
    const requests: InternalAxiosRequestConfig[] = [];
    publicRequestClient.instance.defaults.adapter = (async (config) => {
      requests.push(config);
      if (config.url === '/auth/logout') {
        return createResponse(
          config,
          200,
          successEnvelope<undefined>(undefined),
        );
      }
      return createResponse(
        config,
        200,
        successEnvelope({
          accessToken: 'access-token',
          expiresAtUtc: '2026-08-11T15:00:00Z',
          tokenType: 'Bearer',
        }),
      );
    }) satisfies AxiosAdapter;

    await expect(
      publicPost('/auth/login', { userName: 'operator' }),
    ).resolves.toMatchObject({
      accessToken: 'access-token',
      tokenType: 'Bearer',
    });
    await expect(publicPostVoid('/auth/logout')).resolves.toBeUndefined();

    expect(requests).toHaveLength(2);
    expect(requests[0]).toMatchObject({
      method: 'post',
      url: '/auth/login',
      withCredentials: true,
    });
    expect(JSON.parse(String(requests[0]!.data))).toEqual({
      userName: 'operator',
    });
    expect(requests[1]).toMatchObject({
      method: 'post',
      url: '/auth/logout',
      withCredentials: true,
    });
  });

  it.each([
    [
      401,
      'NovaGame.AdminAccess.InvalidCredentials',
      'The user name or password is invalid.',
    ],
    [
      429,
      'NovaGame.AdminAccess.LoginRateLimited',
      'Too many login attempts. Try again later.',
    ],
  ])(
    'preserves public HTTP %i while normalizing the server envelope',
    async (status, code, message) => {
      publicRequestClient.instance.defaults.adapter = (async (config) => {
        throw createHttpError(config, status, failureEnvelope(code, message));
      }) satisfies AxiosAdapter;

      await expect(
        publicPost('/auth/login', {
          password: 'invalid',
          userName: 'operator',
        }),
      ).rejects.toMatchObject({ code, message, status });
    },
  );

  it('coalesces two concurrent protected 401 responses and retries both', async () => {
    const refresh = createDeferred<
      AxiosResponse<
        ReturnType<
          typeof successEnvelope<{
            accessToken: string;
            expiresAtUtc: string;
            tokenType: string;
          }>
        >
      >
    >();
    let refreshConfig: InternalAxiosRequestConfig | undefined;
    let protectedAttempts = 0;
    let refreshAttempts = 0;

    state.accessStore.accessToken = 'expired-access-token';
    publicRequestClient.instance.defaults.adapter = (async (config) => {
      refreshAttempts += 1;
      refreshConfig = config;
      return refresh.promise;
    }) satisfies AxiosAdapter;
    requestClient.instance.defaults.adapter = (async (config) => {
      protectedAttempts += 1;
      if (readAuthorization(config) === 'Bearer expired-access-token') {
        throw createHttpError(
          config,
          401,
          failureEnvelope(
            'NovaGame.AdminAccess.Unauthorized',
            'Authentication is required.',
          ),
        );
      }
      return createResponse(config, 200, successEnvelope({ path: config.url }));
    }) satisfies AxiosAdapter;

    const first = requestClient.get<{ path: string }>('/protected/first');
    const second = requestClient.get<{ path: string }>('/protected/second');

    await vi.waitFor(() => {
      expect(protectedAttempts).toBe(2);
      expect(refreshAttempts).toBe(1);
      expect(refreshConfig).toBeDefined();
    });
    refresh.resolve(
      createResponse(
        refreshConfig!,
        200,
        successEnvelope({
          accessToken: 'replacement-access-token',
          expiresAtUtc: '2026-08-11T15:15:00Z',
          tokenType: 'Bearer',
        }),
      ),
    );

    await expect(Promise.all([first, second])).resolves.toEqual([
      { path: '/protected/first' },
      { path: '/protected/second' },
    ]);
    expect(protectedAttempts).toBe(4);
    expect(refreshAttempts).toBe(1);
    expect(state.accessStore.accessToken).toBe('replacement-access-token');
    expect(state.accessStore.setRefreshToken).not.toHaveBeenCalled();
    expect(state.expireSession).not.toHaveBeenCalled();
  });

  it('rejects every queued 401 when refresh fails without retrying recursively', async () => {
    const refresh = createDeferred<AxiosResponse>();
    let refreshConfig: InternalAxiosRequestConfig | undefined;
    let protectedAttempts = 0;
    let refreshAttempts = 0;

    state.accessStore.accessToken = 'expired-access-token';
    publicRequestClient.instance.defaults.adapter = (async (config) => {
      refreshAttempts += 1;
      refreshConfig = config;
      return refresh.promise;
    }) satisfies AxiosAdapter;
    requestClient.instance.defaults.adapter = (async (config) => {
      protectedAttempts += 1;
      throw createHttpError(
        config,
        401,
        failureEnvelope(
          'NovaGame.AdminAccess.Unauthorized',
          'Authentication is required.',
        ),
      );
    }) satisfies AxiosAdapter;

    const settled = Promise.allSettled([
      requestClient.get('/protected/first'),
      requestClient.get('/protected/second'),
    ]);

    await vi.waitFor(() => {
      expect(protectedAttempts).toBe(2);
      expect(refreshAttempts).toBe(1);
      expect(refreshConfig).toBeDefined();
    });
    refresh.reject(
      createHttpError(
        refreshConfig!,
        401,
        failureEnvelope(
          'NovaGame.AdminAccess.RefreshTokenExpired',
          'The refresh token has expired.',
        ),
      ),
    );

    const results = await settled;
    expect(results).toHaveLength(2);
    for (const result of results) {
      expect(result).toMatchObject({
        reason: {
          code: 'NovaGame.AdminAccess.RefreshTokenExpired',
          status: 401,
        },
        status: 'rejected',
      });
    }
    expect(protectedAttempts).toBe(2);
    expect(refreshAttempts).toBe(1);
    expect(state.expireSession).toHaveBeenCalledOnce();
    expect(state.accessStore.setAccessToken).not.toHaveBeenCalled();
  });

  it('adds only the access-token bearer, locale, and cookie policy', async () => {
    const requests: InternalAxiosRequestConfig[] = [];
    requestClient.instance.defaults.adapter = (async (config) => {
      requests.push(config);
      return createResponse(config, 200, successEnvelope({ ok: true }));
    }) satisfies AxiosAdapter;

    await requestClient.get('/anonymous');
    state.accessStore.accessToken = 'access-token';
    await requestClient.get('/authenticated');

    expect(readAuthorization(requests[0]!)).toBeNull();
    expect(AxiosHeaders.from(requests[0]!.headers).get('Accept-Language')).toBe(
      'en-US',
    );
    expect(requests[0]!.withCredentials).toBe(true);
    expect(readAuthorization(requests[1]!)).toBe('Bearer access-token');
    expect(requests[1]!.withCredentials).toBe(true);
    expect(state.accessStore.setRefreshToken).not.toHaveBeenCalled();
  });

  it('coalesces direct cookie refreshes and releases the flight afterward', async () => {
    const refresh = createDeferred<AxiosResponse>();
    let refreshConfig: InternalAxiosRequestConfig | undefined;
    let attempts = 0;
    publicRequestClient.instance.defaults.adapter = (async (config) => {
      attempts += 1;
      refreshConfig = config;
      return refresh.promise;
    }) satisfies AxiosAdapter;

    const first = refreshAccessToken();
    const second = refreshAccessToken();
    expect(first).toBe(second);

    await vi.waitFor(() => expect(attempts).toBe(1));
    refresh.resolve(
      createResponse(
        refreshConfig!,
        200,
        successEnvelope({
          accessToken: 'replacement-access-token',
          expiresAtUtc: '2026-08-11T15:15:00Z',
          tokenType: 'Bearer',
        }),
      ),
    );
    await Promise.all([first, second]);

    publicRequestClient.instance.defaults.adapter = (async (config) => {
      attempts += 1;
      return createResponse(
        config,
        200,
        successEnvelope({
          accessToken: 'next-access-token',
          expiresAtUtc: '2026-08-11T15:30:00Z',
          tokenType: 'Bearer',
        }),
      );
    }) satisfies AxiosAdapter;
    await refreshAccessToken();
    expect(attempts).toBe(2);
  });
});
