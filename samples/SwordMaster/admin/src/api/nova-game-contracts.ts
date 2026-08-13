export type Guid = string;
export type UtcDateTime = string;

export interface ApiErrorPayload {
  code: string;
  details: null | Record<string, string[]>;
  message: string;
}

export type ApiResult<T> =
  | { data: T; error: null; succeeded: true }
  | { data?: null; error: ApiErrorPayload; succeeded: false };

export type ApiVoidResult =
  | { error: ApiErrorPayload; succeeded: false }
  | { error: null; succeeded: true };

export interface AccessTokenResponse {
  accessToken: string;
  expiresAtUtc: UtcDateTime;
  tokenType: string;
}

export interface AdministratorInfo {
  displayName: string;
  id: Guid;
  permissionCodes: string[];
  roles: string[];
  userName: string;
}
