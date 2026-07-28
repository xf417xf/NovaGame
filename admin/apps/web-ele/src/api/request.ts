import type { RequestClientOptions } from '@vben/request';

import type {
  AccessTokenResponse,
  ApiResult,
  ApiVoidResult,
} from './nova-game-contracts';

import { useAppConfig } from '@vben/hooks';
import { preferences } from '@vben/preferences';
import { authenticateResponseInterceptor, RequestClient } from '@vben/request';
import { useAccessStore } from '@vben/stores';

import {
  toNovaGameApiError,
  unwrapApiResultBody,
  unwrapApiVoidResultBody,
} from './api-result';

export { NovaGameApiError, toNovaGameApiError } from './api-result';

const { apiURL } = useAppConfig(import.meta.env, import.meta.env.PROD);

interface RawResponse<T> {
  config: { responseReturn?: string };
  data: T;
  headers: Record<string, string | undefined>;
  status: number;
}

export function unwrapApiResult<T>(response: RawResponse<ApiResult<T>>): T {
  return unwrapApiResultBody(response.data, response.status);
}

export function unwrapApiVoidResult(
  response: RawResponse<ApiVoidResult>,
): void {
  unwrapApiVoidResultBody(response.data, response.status);
}

const publicRequestClient = new RequestClient({
  baseURL: apiURL,
  responseReturn: 'raw',
  withCredentials: true,
});

export async function publicPost<T>(url: string, data?: unknown): Promise<T> {
  try {
    const response = await publicRequestClient.post<RawResponse<ApiResult<T>>>(
      url,
      data,
      { withCredentials: true },
    );
    return unwrapApiResult(response);
  } catch (error) {
    throw toNovaGameApiError(error);
  }
}

export async function publicPostVoid(
  url: string,
  data?: unknown,
): Promise<void> {
  try {
    const response = await publicRequestClient.post<RawResponse<ApiVoidResult>>(
      url,
      data,
      { withCredentials: true },
    );
    unwrapApiVoidResult(response);
  } catch (error) {
    throw toNovaGameApiError(error);
  }
}

let refreshPromise: null | Promise<AccessTokenResponse> = null;

export function refreshAccessToken(): Promise<AccessTokenResponse> {
  refreshPromise ??= publicPost<AccessTokenResponse>('/auth/refresh').finally(
    () => {
      refreshPromise = null;
    },
  );
  return refreshPromise;
}

function createRequestClient(baseURL: string, options?: RequestClientOptions) {
  const client = new RequestClient({
    ...options,
    baseURL,
    withCredentials: true,
  });

  client.addRequestInterceptor({
    fulfilled: async (config) => {
      const accessStore = useAccessStore();
      config.headers.Authorization = accessStore.accessToken
        ? `Bearer ${accessStore.accessToken}`
        : null;
      config.headers['Accept-Language'] = preferences.app.locale;
      config.withCredentials = true;
      return config;
    },
  });

  client.addResponseInterceptor({
    fulfilled: (response) => {
      if (response.config.responseReturn === 'raw') {
        return response;
      }
      return unwrapApiResult(
        response as RawResponse<ApiResult<unknown>>,
      ) as unknown as typeof response;
    },
  });

  client.addResponseInterceptor(
    authenticateResponseInterceptor({
      client,
      doReAuthenticate: async () => {
        const { useAuthStore } = await import('#/store');
        await useAuthStore().expireSession();
      },
      doRefreshToken: async () => {
        const accessStore = useAccessStore();
        const session = await refreshAccessToken();
        accessStore.setAccessToken(session.accessToken);
        return session.accessToken;
      },
      enableRefreshToken: true,
      formatToken: (token) => (token ? `Bearer ${token}` : null),
    }),
  );

  client.addResponseInterceptor({
    rejected: (error) => {
      throw toNovaGameApiError(error);
    },
  });

  return client;
}

export function createCorrelationId(): string {
  return (
    globalThis.crypto?.randomUUID?.() ??
    `novagame-${Date.now()}-${Math.random().toString(16).slice(2)}`
  );
}

export const requestClient = createRequestClient(apiURL, {
  responseReturn: 'data',
});
