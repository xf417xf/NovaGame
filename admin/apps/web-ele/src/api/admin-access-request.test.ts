import { describe, expect, it, vi } from 'vitest';

import { NovaGameApiError } from './api-result';

import './request';

type ResponseInterceptor = {
  rejected?: (error: unknown) => unknown;
};

const requestClientState = vi.hoisted(() => ({
  responseInterceptors: [] as ResponseInterceptor[],
}));

vi.mock('@vben/hooks', () => ({
  useAppConfig: () => ({ apiURL: '/api' }),
}));

vi.mock('@vben/preferences', () => ({
  preferences: { app: { locale: 'en-US' } },
}));

vi.mock('@vben/request', () => ({
  authenticateResponseInterceptor: () => ({}),
  RequestClient: class {
    addRequestInterceptor() {}

    addResponseInterceptor(interceptor: ResponseInterceptor) {
      requestClientState.responseInterceptors.push(interceptor);
    }
  },
}));

vi.mock('@vben/stores', () => ({
  useAccessStore: () => ({ accessToken: null, setAccessToken: vi.fn() }),
}));

describe('administrator access request cancellation', () => {
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
});
