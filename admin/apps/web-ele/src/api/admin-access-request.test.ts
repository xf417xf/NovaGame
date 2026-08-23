import { beforeEach, describe, expect, it, vi } from 'vitest';

import { NovaGameApiError } from './api-result';

import './request';

type ResponseInterceptor = {
  rejected?: (error: unknown) => unknown;
};

type AuthenticationResponseInterceptor = {
  doReAuthenticate: () => Promise<void>;
  doRefreshToken: () => Promise<string>;
};

const requestClientState = vi.hoisted(() => ({
  authenticationResponseInterceptor:
    null as AuthenticationResponseInterceptor | null,
  authStore: {
    expireSession: vi.fn(),
    refreshAccessTokenForRequest: vi.fn(),
  },
  accessStore: {
    accessToken: null as null | string,
    setAccessToken: vi.fn(),
  },
  responseInterceptors: [] as ResponseInterceptor[],
}));

vi.mock('@vben/hooks', () => ({
  useAppConfig: () => ({ apiURL: '/api' }),
}));

vi.mock('@vben/preferences', () => ({
  preferences: { app: { locale: 'en-US' } },
}));

vi.mock('@vben/request', () => ({
  authenticateResponseInterceptor: (
    interceptor: AuthenticationResponseInterceptor,
  ) => {
    requestClientState.authenticationResponseInterceptor = interceptor;
    return {};
  },
  RequestClient: class {
    addRequestInterceptor() {}

    addResponseInterceptor(interceptor: ResponseInterceptor) {
      requestClientState.responseInterceptors.push(interceptor);
    }
  },
}));

vi.mock('@vben/stores', () => ({
  useAccessStore: () => requestClientState.accessStore,
}));

vi.mock('#/store', () => ({
  useAuthStore: () => requestClientState.authStore,
}));

describe('administrator access request cancellation', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    requestClientState.accessStore.accessToken = null;
  });

  it('rethrows cancellation from the registered production response interceptor', () => {
    const rejected = requestClientState.responseInterceptors.at(-1)?.rejected;
    expect(rejected).toBeTypeOf('function');
    if (!rejected) {
      throw new Error(
        'The request client did not register a rejection interceptor.',
      );
    }

    const cancelled = Object.assign(new Error('The request was cancelled.'), {
      code: 'ERR_CANCELED',
      name: 'CanceledError',
    });
    let received: unknown;
    try {
      rejected(cancelled);
    } catch (error) {
      received = error;
    }

    expect(received).toBe(cancelled);
    expect(received).not.toBeInstanceOf(NovaGameApiError);
  });

  it('delegates response refresh token application to the auth lifecycle', async () => {
    const interceptor = requestClientState.authenticationResponseInterceptor;
    expect(interceptor).not.toBeNull();
    if (!interceptor) {
      throw new Error(
        'The request client did not register an authentication interceptor.',
      );
    }
    requestClientState.authStore.refreshAccessTokenForRequest.mockResolvedValue(
      'coordinated-access-token',
    );

    await expect(interceptor.doRefreshToken()).resolves.toBe(
      'coordinated-access-token',
    );

    expect(
      requestClientState.authStore.refreshAccessTokenForRequest,
    ).toHaveBeenCalledTimes(1);
    expect(
      requestClientState.accessStore.setAccessToken,
    ).not.toHaveBeenCalled();
  });
});
