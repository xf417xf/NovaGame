import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import {
  changeAdministratorDisplayName,
  createAdministrator,
  createAdminRole,
  getAdministrator,
  getAdminRole,
  getPermissionCatalog,
  listAdministrators,
  listAdminRoles,
  listAuditRecords,
  listSessionFamilies,
  renameAdminRole,
  replaceAdministratorRoles,
  replaceAdminRolePermissions,
  resetAdministratorPassword,
  revokeSessionFamily,
  setAdministratorActiveState,
  setAdminRoleActiveState,
} from '.';
import { isNovaGameRequestCancelled, NovaGameApiError } from '../api-result';

const entityHeaders: Record<string, string> = { ETag: '"7"' };

const requestClient = vi.hoisted(() => ({
  get: vi.fn(),
  post: vi.fn(),
  put: vi.fn(),
}));

vi.mock('../request', () => ({
  requestClient,
  unwrapApiResult: <T>(response: { data: { data: T } }) => response.data.data,
}));

function successfulResponse<T>(
  data: T,
  headers: Record<string, string> = entityHeaders,
) {
  return {
    config: { responseReturn: 'raw' },
    data: { data, error: null, succeeded: true },
    headers,
    status: 200,
  };
}

beforeEach(() => {
  vi.clearAllMocks();
  requestClient.get.mockImplementation(() =>
    Promise.resolve(successfulResponse({})),
  );
  requestClient.post.mockImplementation(() =>
    Promise.resolve(successfulResponse({})),
  );
  requestClient.put.mockImplementation(() =>
    Promise.resolve(successfulResponse({})),
  );
});

afterEach(() => {
  vi.restoreAllMocks();
});

describe('administrator access transport', () => {
  it('maps every generated operation to the API base-relative path and exact headers', async () => {
    const controller = new AbortController();
    const administratorId = 'administrator/id';
    const roleId = 'role/id';
    const familyId = 'family/id';
    const etag = '"7"';
    const idempotencyKey = 'b8e4bec7-a2d0-4ebb-b4e4-dae4e01d0c83';
    const created = await createAdministrator(
      {
        displayName: 'Operator',
        initialPassword: 'Not retained by the test result.',
        roleIds: [roleId],
        userName: 'operator',
      },
      idempotencyKey,
    );

    await listAdministrators(
      {
        IsActive: true,
        Page: 1,
        PageSize: 20,
        RoleId: roleId,
        Search: 'operator',
      },
      { signal: controller.signal },
    );
    await getAdministrator(administratorId);
    await changeAdministratorDisplayName(
      administratorId,
      { displayName: 'Updated operator' },
      etag,
    );
    await replaceAdministratorRoles(
      administratorId,
      { roleIds: [roleId] },
      etag,
    );
    await setAdministratorActiveState(
      administratorId,
      { isActive: false },
      etag,
    );
    await resetAdministratorPassword(
      administratorId,
      { newPassword: 'Also not retained by the test result.' },
      etag,
      idempotencyKey,
    );
    await listAdminRoles({
      IsActive: true,
      IsBuiltIn: false,
      Page: 1,
      PageSize: 20,
    });
    await createAdminRole(
      { name: 'Operators', permissionNames: [] },
      idempotencyKey,
    );
    await getAdminRole(roleId);
    await renameAdminRole(roleId, { name: 'Renamed operators' }, etag);
    await setAdminRoleActiveState(roleId, { isActive: false }, etag);
    await replaceAdminRolePermissions(roleId, { permissionNames: [] }, etag);
    await getPermissionCatalog();
    await listSessionFamilies({
      AdministratorId: administratorId,
      Page: 1,
      PageSize: 20,
      State: 1,
    });
    await revokeSessionFamily(familyId);
    await listAuditRecords({
      FromUtc: '2026-08-01T00:00:00.000Z',
      Page: 1,
      PageSize: 20,
      Succeeded: true,
      ToUtc: '2026-08-08T00:00:00.000Z',
    });

    expect(created).toMatchObject({
      etag,
      headers: { ETag: etag },
      status: 200,
    });

    expect(requestClient.get).toHaveBeenNthCalledWith(
      1,
      '/admin-access/administrators',
      {
        params: {
          IsActive: true,
          Page: 1,
          PageSize: 20,
          RoleId: roleId,
          Search: 'operator',
        },
        responseReturn: 'raw',
        signal: controller.signal,
      },
    );
    expect(requestClient.get).toHaveBeenNthCalledWith(
      2,
      '/admin-access/administrators/administrator%2Fid',
      { responseReturn: 'raw' },
    );
    expect(requestClient.get).toHaveBeenNthCalledWith(
      3,
      '/admin-access/roles',
      {
        params: { IsActive: true, IsBuiltIn: false, Page: 1, PageSize: 20 },
        responseReturn: 'raw',
      },
    );
    expect(requestClient.get).toHaveBeenNthCalledWith(
      4,
      '/admin-access/roles/role%2Fid',
      { responseReturn: 'raw' },
    );
    expect(requestClient.get).toHaveBeenNthCalledWith(
      5,
      '/admin-access/permissions',
      { responseReturn: 'raw' },
    );
    expect(requestClient.get).toHaveBeenNthCalledWith(
      6,
      '/admin-access/sessions',
      {
        params: {
          AdministratorId: administratorId,
          Page: 1,
          PageSize: 20,
          State: 1,
        },
        responseReturn: 'raw',
      },
    );
    expect(requestClient.get).toHaveBeenNthCalledWith(
      7,
      '/admin-access/audit-records',
      {
        params: {
          FromUtc: '2026-08-01T00:00:00.000Z',
          Page: 1,
          PageSize: 20,
          Succeeded: true,
          ToUtc: '2026-08-08T00:00:00.000Z',
        },
        responseReturn: 'raw',
      },
    );

    expect(requestClient.post).toHaveBeenNthCalledWith(
      1,
      '/admin-access/administrators',
      {
        displayName: 'Operator',
        initialPassword: 'Not retained by the test result.',
        roleIds: [roleId],
        userName: 'operator',
      },
      {
        headers: { 'Idempotency-Key': idempotencyKey },
        responseReturn: 'raw',
      },
    );
    expect(requestClient.post).toHaveBeenNthCalledWith(
      2,
      '/admin-access/administrators/administrator%2Fid/password-reset',
      { newPassword: 'Also not retained by the test result.' },
      {
        headers: { 'Idempotency-Key': idempotencyKey, 'If-Match': etag },
        responseReturn: 'raw',
      },
    );
    expect(requestClient.post).toHaveBeenNthCalledWith(
      3,
      '/admin-access/roles',
      { name: 'Operators', permissionNames: [] },
      {
        headers: { 'Idempotency-Key': idempotencyKey },
        responseReturn: 'raw',
      },
    );
    expect(requestClient.post).toHaveBeenNthCalledWith(
      4,
      '/admin-access/sessions/family%2Fid/revoke',
      undefined,
      { responseReturn: 'raw' },
    );

    expect(requestClient.put).toHaveBeenNthCalledWith(
      1,
      '/admin-access/administrators/administrator%2Fid/display-name',
      { displayName: 'Updated operator' },
      { headers: { 'If-Match': etag }, responseReturn: 'raw' },
    );
    expect(requestClient.put).toHaveBeenNthCalledWith(
      2,
      '/admin-access/administrators/administrator%2Fid/roles',
      { roleIds: [roleId] },
      { headers: { 'If-Match': etag }, responseReturn: 'raw' },
    );
    expect(requestClient.put).toHaveBeenNthCalledWith(
      3,
      '/admin-access/administrators/administrator%2Fid/active-state',
      { isActive: false },
      { headers: { 'If-Match': etag }, responseReturn: 'raw' },
    );
    expect(requestClient.put).toHaveBeenNthCalledWith(
      4,
      '/admin-access/roles/role%2Fid/name',
      { name: 'Renamed operators' },
      { headers: { 'If-Match': etag }, responseReturn: 'raw' },
    );
    expect(requestClient.put).toHaveBeenNthCalledWith(
      5,
      '/admin-access/roles/role%2Fid/active-state',
      { isActive: false },
      { headers: { 'If-Match': etag }, responseReturn: 'raw' },
    );
    expect(requestClient.put).toHaveBeenNthCalledWith(
      6,
      '/admin-access/roles/role%2Fid/permissions',
      { permissionNames: [] },
      { headers: { 'If-Match': etag }, responseReturn: 'raw' },
    );
  });

  it('preserves server status, code, and field details', async () => {
    requestClient.get.mockRejectedValueOnce({
      response: {
        data: {
          error: {
            code: 'NovaGame.AdminAccess.RevisionConflict',
            details: { expectedRevision: ['Reload the administrator.'] },
            message: 'The administrator changed.',
          },
          succeeded: false,
        },
        status: 409,
      },
    });

    await expect(getAdministrator('administrator-id')).rejects.toEqual(
      expect.objectContaining({
        code: 'NovaGame.AdminAccess.RevisionConflict',
        details: { expectedRevision: ['Reload the administrator.'] },
        status: 409,
      }),
    );
  });

  it('keeps a cancelled request distinct from a network API error', async () => {
    const cancelled = Object.assign(new Error('The request was cancelled.'), {
      code: 'ERR_CANCELED',
      name: 'CanceledError',
    });
    requestClient.get.mockRejectedValueOnce(cancelled);

    expect(isNovaGameRequestCancelled(cancelled)).toBe(true);
    await expect(getPermissionCatalog()).rejects.toBe(cancelled);
    expect(cancelled).not.toBeInstanceOf(NovaGameApiError);
  });
});
