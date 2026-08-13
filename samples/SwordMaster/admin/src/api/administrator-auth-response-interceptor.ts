import type { InternalAxiosRequestConfig } from '@vben/request';
import type { RequestClient } from '@vben/request';

import { AxiosHeaders } from '@vben/request';

const authenticationRetryMarker = '__swordMasterAuthenticationRetry';

interface AuthenticationRetryConfig extends InternalAxiosRequestConfig {
  [authenticationRetryMarker]?: boolean;
}

interface AuthenticationResponseError {
  config?: AuthenticationRetryConfig;
  response?: {
    status?: number;
  };
}

interface AdministratorAuthenticationInterceptorOptions {
  client: Pick<RequestClient, 'request'>;
  doReAuthenticate: () => Promise<void>;
  doRefreshToken: () => Promise<string>;
  formatToken: (token: string) => null | string;
}

function asAuthenticationResponseError(
  error: unknown,
): AuthenticationResponseError {
  return error && typeof error === 'object'
    ? (error as AuthenticationResponseError)
    : {};
}

async function reAuthenticateAndReject(
  error: unknown,
  doReAuthenticate: () => Promise<void>,
): Promise<never> {
  try {
    await doReAuthenticate();
  } catch {
    // Preserve the request or refresh failure that made the session unusable.
  }
  throw error;
}

export function createAdministratorAuthenticationResponseInterceptor(
  options: AdministratorAuthenticationInterceptorOptions,
) {
  let refreshFlight: null | Promise<string> = null;

  function refreshOnce(): Promise<string> {
    refreshFlight ??= (async () => {
      try {
        const token = await options.doRefreshToken();
        if (!token) {
          throw new Error(
            'The administrator refresh response had no access token.',
          );
        }
        return token;
      } catch (error) {
        return reAuthenticateAndReject(error, options.doReAuthenticate);
      }
    })().finally(() => {
      refreshFlight = null;
    });

    return refreshFlight;
  }

  return {
    rejected: async (error: unknown) => {
      const responseError = asAuthenticationResponseError(error);
      if (responseError.response?.status !== 401) {
        throw error;
      }

      const config = responseError.config;
      if (!config?.url || config[authenticationRetryMarker]) {
        return reAuthenticateAndReject(error, options.doReAuthenticate);
      }

      const token = await refreshOnce();
      const headers = AxiosHeaders.from(config.headers);
      headers.set('Authorization', options.formatToken(token));

      const retryConfig: AuthenticationRetryConfig = {
        ...config,
        [authenticationRetryMarker]: true,
        headers,
      };
      return options.client.request(config.url, retryConfig);
    },
  };
}
