import type { RequestClient } from './request-client';
import type { MakeErrorMessageFn, ResponseInterceptorConfig } from './types';

import { $t } from '@vben/locales';
import { isFunction } from '@vben/utils';

import axios from 'axios';

export const defaultResponseInterceptor = ({
  codeField = 'code',
  dataField = 'data',
  successCode = 0,
}: {
  /** 响应数据中代表访问结果的字段名 */
  codeField: string;
  /** 响应数据中装载实际数据的字段名，或者提供一个函数从响应数据中解析需要返回的数据 */
  dataField: ((response: any) => any) | string;
  /** 当codeField所指定的字段值与successCode相同时，代表接口访问成功。如果提供一个函数，则返回true代表接口访问成功 */
  successCode: ((code: any) => boolean) | number | string;
}): ResponseInterceptorConfig => {
  return {
    fulfilled: (response) => {
      const { config, data: responseData, status } = response;

      if (config.responseReturn === 'raw') {
        return response;
      }

      if (status >= 200 && status < 400) {
        if (config.responseReturn === 'body') {
          return responseData;
        } else if (
          isFunction(successCode)
            ? successCode(responseData[codeField])
            : responseData[codeField] === successCode
        ) {
          return isFunction(dataField)
            ? dataField(responseData)
            : responseData[dataField];
        }
      }
      throw Object.assign({}, response, { response });
    },
  };
};

export const authenticateResponseInterceptor = ({
  client,
  doReAuthenticate,
  doRefreshToken,
  enableRefreshToken,
  formatToken,
}: {
  client: RequestClient;
  doReAuthenticate: () => Promise<void>;
  doRefreshToken: () => Promise<string>;
  enableRefreshToken: boolean;
  formatToken: (token: string) => null | string;
}): ResponseInterceptorConfig => {
  let reauthenticationPromise: null | Promise<void> = null;
  let reauthenticationSettled = false;

  function clearSettledReauthentication() {
    if (
      reauthenticationPromise &&
      reauthenticationSettled &&
      !client.isRefreshing
    ) {
      reauthenticationPromise = null;
      reauthenticationSettled = false;
    }
  }

  function markReauthenticationSettled(pendingReauthentication: Promise<void>) {
    if (reauthenticationPromise !== pendingReauthentication) {
      return;
    }

    reauthenticationSettled = true;
    clearSettledReauthentication();
  }

  function getReauthenticationPromise() {
    if (!reauthenticationPromise) {
      const pendingReauthentication = doReAuthenticate();
      reauthenticationPromise = pendingReauthentication;
      reauthenticationSettled = false;
      void pendingReauthentication.then(
        () => markReauthenticationSettled(pendingReauthentication),
        () => markReauthenticationSettled(pendingReauthentication),
      );
    }

    return reauthenticationPromise;
  }

  async function reauthenticate() {
    const pendingReauthentication = getReauthenticationPromise();
    try {
      await pendingReauthentication;
    } finally {
      clearSettledReauthentication();
    }
  }

  return {
    rejected: async (error) => {
      const { config, response } = error;
      // 如果不是 401 错误，直接抛出异常
      if (response?.status !== 401) {
        throw error;
      }
      // 判断是否启用了 refreshToken 功能
      // 如果没有启用或者已经是重试请求了，直接跳转到重新登录
      if (!enableRefreshToken || config.__isRetryRequest) {
        await reauthenticate();
        throw error;
      }
      if (reauthenticationPromise) {
        await reauthenticate();
        throw error;
      }
      // 如果正在刷新 token，则将请求加入队列，等待刷新完成
      if (client.isRefreshing) {
        return new Promise((resolve, reject) => {
          client.refreshTokenQueue.push((result) => {
            if (result.kind === 'failure') {
              reject(result.error);
              return;
            }
            config.headers.Authorization = formatToken(result.token);
            resolve(client.request(config.url, { ...config }));
          });
        });
      }

      // 标记开始刷新 token
      client.isRefreshing = true;
      // 标记当前请求为重试请求，避免无限循环
      config.__isRetryRequest = true;

      try {
        const newToken = await doRefreshToken();

        // 处理队列中的请求
        client.refreshTokenQueue.forEach((callback) =>
          callback({ kind: 'success', token: newToken }),
        );
        // 清空队列
        client.refreshTokenQueue = [];

        return client.request(error.config.url, { ...error.config });
      } catch (refreshError) {
        // 如果刷新 token 失败，处理错误（如强制登出或跳转登录页面）
        client.refreshTokenQueue.forEach((callback) =>
          callback({ error: refreshError, kind: 'failure' }),
        );
        client.refreshTokenQueue = [];
        if (axios.isCancel(refreshError)) {
          throw refreshError;
        }
        console.error('Refresh token failed, please login again.');
        const pendingReauthentication = getReauthenticationPromise();
        await pendingReauthentication;

        throw refreshError;
      } finally {
        client.isRefreshing = false;
        clearSettledReauthentication();
      }
    },
  };
};

export const errorMessageResponseInterceptor = (
  makeErrorMessage?: MakeErrorMessageFn,
): ResponseInterceptorConfig => {
  return {
    rejected: (error: any) => {
      if (axios.isCancel(error)) {
        return Promise.reject(error);
      }

      const err: string = error?.toString?.() ?? '';
      let errMsg = '';
      if (err?.includes('Network Error')) {
        errMsg = $t('ui.fallback.http.networkError');
      } else if (error?.message?.includes?.('timeout')) {
        errMsg = $t('ui.fallback.http.requestTimeout');
      }
      if (errMsg) {
        makeErrorMessage?.(errMsg, error);
        return Promise.reject(error);
      }

      let errorMessage: string;
      const status = error?.response?.status;

      switch (status) {
        case 400: {
          errorMessage = $t('ui.fallback.http.badRequest');
          break;
        }
        case 401: {
          errorMessage = $t('ui.fallback.http.unauthorized');
          break;
        }
        case 403: {
          errorMessage = $t('ui.fallback.http.forbidden');
          break;
        }
        case 404: {
          errorMessage = $t('ui.fallback.http.notFound');
          break;
        }
        case 408: {
          errorMessage = $t('ui.fallback.http.requestTimeout');
          break;
        }
        default: {
          errorMessage = $t('ui.fallback.http.internalServerError');
        }
      }
      makeErrorMessage?.(errorMessage, error);
      return Promise.reject(error);
    },
  };
};
