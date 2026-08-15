import type { RouteRecordRaw } from 'vue-router';

import { describe, expect, it } from 'vitest';

import {
  AdminAccessPermission,
  hasAdminAccessPermission,
} from '#/features/admin-access/permissions';

import adminAccessRoutes, {
  administratorAccessRouteName,
  filterAdminAccessRoutes,
} from './routes/modules/admin-access';

const expectedPermissionCodes = [
  'Operations.AdminAccess.Access',
  'Operations.AdminAccess.Administrators.Read',
  'Operations.AdminAccess.Administrators.Manage',
  'Operations.AdminAccess.Administrators.ResetPassword',
  'Operations.AdminAccess.Roles.Read',
  'Operations.AdminAccess.Roles.Manage',
  'Operations.AdminAccess.Sessions.Read',
  'Operations.AdminAccess.Sessions.Revoke',
  'Operations.AdminAccess.AuditLogs.Read',
];

function filteredGroup(accessCodes: readonly string[]) {
  return filterAdminAccessRoutes(adminAccessRoutes, accessCodes).find(
    (route) => route.name === administratorAccessRouteName,
  );
}

describe('administrator access static routes', () => {
  it('defines the exact server permission codes and retains reset as a separate leaf', () => {
    expect(Object.values(AdminAccessPermission).toSorted()).toEqual(
      expectedPermissionCodes.toSorted(),
    );
    expect(
      hasAdminAccessPermission(
        [
          AdminAccessPermission.Access,
          AdminAccessPermission.AdministratorsResetPassword,
        ],
        AdminAccessPermission.AdministratorsResetPassword,
      ),
    ).toBe(true);
    expect(
      hasAdminAccessPermission(
        [
          AdminAccessPermission.Access,
          AdminAccessPermission.AdministratorsManage,
        ],
        AdminAccessPermission.AdministratorsResetPassword,
      ),
    ).toBe(false);
  });

  it('fails closed when either Access or the required read leaf is absent', () => {
    expect(
      filterAdminAccessRoutes(adminAccessRoutes, [
        AdminAccessPermission.Access,
      ]),
    ).toEqual([]);
    expect(
      filterAdminAccessRoutes(adminAccessRoutes, [
        AdminAccessPermission.RolesRead,
      ]),
    ).toEqual([]);
  });

  it.each([
    [
      AdminAccessPermission.AdministratorsRead,
      'AdminAccessAdministrators',
      '/admin-access/administrators',
    ],
    [
      AdminAccessPermission.RolesRead,
      'AdminAccessRoles',
      '/admin-access/roles',
    ],
    [
      AdminAccessPermission.SessionsRead,
      'AdminAccessSessions',
      '/admin-access/sessions',
    ],
    [
      AdminAccessPermission.AuditLogsRead,
      'AdminAccessAudit',
      '/admin-access/audit',
    ],
  ] as const)(
    'keeps only %s when it is paired with Access',
    (leafPermission, routeName, path) => {
      const group = filteredGroup([
        AdminAccessPermission.Access,
        leafPermission,
      ]);

      expect(group).toMatchObject({ redirect: path });
      expect(group?.children?.map((child) => child.name)).toEqual([routeName]);
      expect(group?.meta?.authority).toBeUndefined();
      expect(group?.children?.[0]?.meta?.authority).toBeUndefined();
    },
  );

  it('preserves the frozen child order and redirects to the first authorized route', () => {
    const group = filteredGroup([
      AdminAccessPermission.Access,
      AdminAccessPermission.AuditLogsRead,
      AdminAccessPermission.RolesRead,
    ]);

    expect(group?.children?.map((child) => child.name)).toEqual([
      'AdminAccessRoles',
      'AdminAccessAudit',
    ]);
    expect(group?.redirect).toBe('/admin-access/roles');
  });

  it('does not alter unrelated static routes', () => {
    const unrelatedRoute = {
      name: 'UnrelatedRoute',
      path: '/unrelated',
    } as RouteRecordRaw;
    const filtered = filterAdminAccessRoutes(
      [...adminAccessRoutes, unrelatedRoute],
      [AdminAccessPermission.Access],
    );

    expect(filtered).toEqual([unrelatedRoute]);
    expect(filtered[0]).toBe(unrelatedRoute);
  });
});
