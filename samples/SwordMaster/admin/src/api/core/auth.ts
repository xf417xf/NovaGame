import type { AccessTokenResponse } from '../nova-game-contracts';

import {
  publicPost,
  publicPostVoid,
  refreshAccessToken,
  requestClient,
} from '#/api/request';

export namespace AuthApi {
  /** 登录接口参数 */
  export interface LoginParams {
    password: string;
    username: string;
  }
}

export async function loginApi(data: AuthApi.LoginParams) {
  return publicPost<AccessTokenResponse>('/auth/login', {
    password: data.password,
    userName: data.username,
  });
}

export async function refreshTokenApi() {
  return refreshAccessToken();
}

export async function logoutApi() {
  return publicPostVoid('/auth/logout');
}

export async function getAccessCodesApi() {
  return requestClient.get<string[]>('/auth/codes');
}
