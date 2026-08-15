import type {
  AdminAccessAuditActionDto,
  AdminAccessAuditRecordDtoAdminAccessPageDto,
  AdministratorDto,
  AdministratorDtoAdminAccessPageDto,
  AdminRoleDto,
  AdminRoleSummaryDtoAdminAccessPageDto,
  AdminSessionFamilyDto,
  AdminSessionFamilyDtoAdminAccessPageDto,
  AdminSessionFamilyState,
  ChangeAdministratorDisplayNameRequest,
  CreateAdministratorRequest,
  CreateAdminRoleRequest,
  GetApiAdminAccessAdministratorsData,
  GetApiAdminAccessAuditRecordsData,
  GetApiAdminAccessRolesData,
  GetApiAdminAccessSessionsData,
  PermissionGroupDto,
  RenameAdminRoleRequest,
  ReplaceAdministratorRolesRequest,
  ReplaceAdminRolePermissionsRequest,
  ResetAdministratorPasswordRequest,
  SetAdministratorActiveStateRequest,
  SetAdminRoleActiveStateRequest,
} from '../../generated/admin-access/types.gen';
import type { ApiResult } from '../nova-game-contracts';
import type { NovaGameRawResponse } from '../request';

import {
  isNovaGameRequestCancelled,
  NovaGameApiError,
  toNovaGameApiError,
} from '../api-result';
import { requestClient, unwrapApiResult } from '../request';

type AdminAccessEnvelope<T> = {
  data?: T;
  error?: unknown;
  succeeded?: boolean;
};

export interface AdminAccessRequestOptions {
  signal?: AbortSignal;
}

export interface AdminAccessResponse<T> {
  data: T;
  headers: Readonly<Record<string, unknown>>;
  status: number;
}

export interface AdminAccessEntityResponse<T> extends AdminAccessResponse<T> {
  etag: string;
}

export type ListAdministratorsRequest = NonNullable<
  GetApiAdminAccessAdministratorsData['query']
>;

export type ListAdminRolesRequest = NonNullable<
  GetApiAdminAccessRolesData['query']
>;

export type ListSessionFamiliesRequest = NonNullable<
  GetApiAdminAccessSessionsData['query']
>;

export type ListAuditRecordsRequest = Omit<
  NonNullable<GetApiAdminAccessAuditRecordsData['query']>,
  'FromUtc' | 'ToUtc'
> & {
  FromUtc: string;
  ToUtc: string;
};

function requestOptions(options?: AdminAccessRequestOptions) {
  return {
    ...(options?.signal ? { signal: options.signal } : {}),
    responseReturn: 'raw' as const,
  };
}

function requestOptionsWithHeaders(
  headers: Record<string, string>,
  options?: AdminAccessRequestOptions,
) {
  return {
    ...requestOptions(options),
    headers,
  };
}

function readHeader(
  headers: Readonly<Record<string, unknown>>,
  name: string,
): null | string {
  const headerReader = headers as Readonly<Record<string, unknown>> & {
    get?: (headerName: string) => unknown;
  };
  const directValue = headerReader.get?.(name);
  const entry = Object.entries(headers).find(
    ([headerName]) => headerName.toLowerCase() === name.toLowerCase(),
  );
  const value = directValue ?? entry?.[1];

  return typeof value === 'string' && value.length > 0 ? value : null;
}

function unwrapResponse<T>(
  response: NovaGameRawResponse<AdminAccessEnvelope<T>>,
): AdminAccessResponse<T> {
  return {
    data: unwrapApiResult(
      response as unknown as NovaGameRawResponse<ApiResult<T>>,
    ),
    headers: response.headers,
    status: response.status,
  };
}

function unwrapEntityResponse<T>(
  response: NovaGameRawResponse<AdminAccessEnvelope<T>>,
): AdminAccessEntityResponse<T> {
  const result = unwrapResponse(response);
  const etag = readHeader(result.headers, 'etag');

  if (!etag || !/^"[1-9]\d*"$/.test(etag)) {
    throw new NovaGameApiError(
      'The NovaGame API response is missing a strong entity ETag.',
      'NovaGame.ContractViolation',
      result.status,
    );
  }

  return { ...result, etag };
}

async function requestResponse<T>(
  request: () => Promise<NovaGameRawResponse<AdminAccessEnvelope<T>>>,
): Promise<AdminAccessResponse<T>> {
  try {
    return unwrapResponse(await request());
  } catch (error) {
    if (isNovaGameRequestCancelled(error)) {
      throw error;
    }
    throw toNovaGameApiError(error);
  }
}

async function requestEntityResponse<T>(
  request: () => Promise<NovaGameRawResponse<AdminAccessEnvelope<T>>>,
): Promise<AdminAccessEntityResponse<T>> {
  try {
    return unwrapEntityResponse(await request());
  } catch (error) {
    if (isNovaGameRequestCancelled(error)) {
      throw error;
    }
    throw toNovaGameApiError(error);
  }
}

function administratorPath(administratorId: string): string {
  return `/admin-access/administrators/${encodeURIComponent(administratorId)}`;
}

function rolePath(roleId: string): string {
  return `/admin-access/roles/${encodeURIComponent(roleId)}`;
}

function sessionPath(familyId: string): string {
  return `/admin-access/sessions/${encodeURIComponent(familyId)}`;
}

export function listAdministrators(
  query: ListAdministratorsRequest,
  options?: AdminAccessRequestOptions,
) {
  return requestResponse<AdministratorDtoAdminAccessPageDto>(() =>
    requestClient.get('/admin-access/administrators', {
      ...requestOptions(options),
      params: query,
    }),
  );
}

export function createAdministrator(
  request: CreateAdministratorRequest,
  idempotencyKey: string,
  options?: AdminAccessRequestOptions,
) {
  return requestEntityResponse<AdministratorDto>(() =>
    requestClient.post('/admin-access/administrators', request, {
      ...requestOptionsWithHeaders(
        { 'Idempotency-Key': idempotencyKey },
        options,
      ),
    }),
  );
}

export function getAdministrator(
  administratorId: string,
  options?: AdminAccessRequestOptions,
) {
  return requestEntityResponse<AdministratorDto>(() =>
    requestClient.get(
      administratorPath(administratorId),
      requestOptions(options),
    ),
  );
}

export function changeAdministratorDisplayName(
  administratorId: string,
  request: ChangeAdministratorDisplayNameRequest,
  etag: string,
  options?: AdminAccessRequestOptions,
) {
  return requestEntityResponse<AdministratorDto>(() =>
    requestClient.put(
      `${administratorPath(administratorId)}/display-name`,
      request,
      requestOptionsWithHeaders({ 'If-Match': etag }, options),
    ),
  );
}

export function replaceAdministratorRoles(
  administratorId: string,
  request: ReplaceAdministratorRolesRequest,
  etag: string,
  options?: AdminAccessRequestOptions,
) {
  return requestEntityResponse<AdministratorDto>(() =>
    requestClient.put(
      `${administratorPath(administratorId)}/roles`,
      request,
      requestOptionsWithHeaders({ 'If-Match': etag }, options),
    ),
  );
}

export function setAdministratorActiveState(
  administratorId: string,
  request: SetAdministratorActiveStateRequest,
  etag: string,
  options?: AdminAccessRequestOptions,
) {
  return requestEntityResponse<AdministratorDto>(() =>
    requestClient.put(
      `${administratorPath(administratorId)}/active-state`,
      request,
      requestOptionsWithHeaders({ 'If-Match': etag }, options),
    ),
  );
}

export function resetAdministratorPassword(
  administratorId: string,
  request: ResetAdministratorPasswordRequest,
  etag: string,
  idempotencyKey: string,
  options?: AdminAccessRequestOptions,
) {
  return requestEntityResponse<AdministratorDto>(() =>
    requestClient.post(
      `${administratorPath(administratorId)}/password-reset`,
      request,
      requestOptionsWithHeaders(
        {
          'Idempotency-Key': idempotencyKey,
          'If-Match': etag,
        },
        options,
      ),
    ),
  );
}

export function listAdminRoles(
  query: ListAdminRolesRequest,
  options?: AdminAccessRequestOptions,
) {
  return requestResponse<AdminRoleSummaryDtoAdminAccessPageDto>(() =>
    requestClient.get('/admin-access/roles', {
      ...requestOptions(options),
      params: query,
    }),
  );
}

export function createAdminRole(
  request: CreateAdminRoleRequest,
  idempotencyKey: string,
  options?: AdminAccessRequestOptions,
) {
  return requestEntityResponse<AdminRoleDto>(() =>
    requestClient.post('/admin-access/roles', request, {
      ...requestOptionsWithHeaders(
        { 'Idempotency-Key': idempotencyKey },
        options,
      ),
    }),
  );
}

export function getAdminRole(
  roleId: string,
  options?: AdminAccessRequestOptions,
) {
  return requestEntityResponse<AdminRoleDto>(() =>
    requestClient.get(rolePath(roleId), requestOptions(options)),
  );
}

export function renameAdminRole(
  roleId: string,
  request: RenameAdminRoleRequest,
  etag: string,
  options?: AdminAccessRequestOptions,
) {
  return requestEntityResponse<AdminRoleDto>(() =>
    requestClient.put(
      `${rolePath(roleId)}/name`,
      request,
      requestOptionsWithHeaders({ 'If-Match': etag }, options),
    ),
  );
}

export function setAdminRoleActiveState(
  roleId: string,
  request: SetAdminRoleActiveStateRequest,
  etag: string,
  options?: AdminAccessRequestOptions,
) {
  return requestEntityResponse<AdminRoleDto>(() =>
    requestClient.put(
      `${rolePath(roleId)}/active-state`,
      request,
      requestOptionsWithHeaders({ 'If-Match': etag }, options),
    ),
  );
}

export function replaceAdminRolePermissions(
  roleId: string,
  request: ReplaceAdminRolePermissionsRequest,
  etag: string,
  options?: AdminAccessRequestOptions,
) {
  return requestEntityResponse<AdminRoleDto>(() =>
    requestClient.put(
      `${rolePath(roleId)}/permissions`,
      request,
      requestOptionsWithHeaders({ 'If-Match': etag }, options),
    ),
  );
}

export function getPermissionCatalog(options?: AdminAccessRequestOptions) {
  return requestResponse<PermissionGroupDto[]>(() =>
    requestClient.get('/admin-access/permissions', requestOptions(options)),
  );
}

export function listSessionFamilies(
  query: ListSessionFamiliesRequest,
  options?: AdminAccessRequestOptions,
) {
  return requestResponse<AdminSessionFamilyDtoAdminAccessPageDto>(() =>
    requestClient.get('/admin-access/sessions', {
      ...requestOptions(options),
      params: query,
    }),
  );
}

export function revokeSessionFamily(
  familyId: string,
  options?: AdminAccessRequestOptions,
) {
  return requestResponse<AdminSessionFamilyDto>(() =>
    requestClient.post(
      `${sessionPath(familyId)}/revoke`,
      undefined,
      requestOptions(options),
    ),
  );
}

export function listAuditRecords(
  query: ListAuditRecordsRequest,
  options?: AdminAccessRequestOptions,
) {
  return requestResponse<AdminAccessAuditRecordDtoAdminAccessPageDto>(() =>
    requestClient.get('/admin-access/audit-records', {
      ...requestOptions(options),
      params: query,
    }),
  );
}

export type {
  AdminAccessAuditActionDto,
  AdminSessionFamilyState,
  ChangeAdministratorDisplayNameRequest,
  CreateAdministratorRequest,
  CreateAdminRoleRequest,
  RenameAdminRoleRequest,
  ReplaceAdministratorRolesRequest,
  ReplaceAdminRolePermissionsRequest,
  ResetAdministratorPasswordRequest,
  SetAdministratorActiveStateRequest,
  SetAdminRoleActiveStateRequest,
};
