export const AdminAccessPermission = {
  Access: 'Operations.AdminAccess.Access',
  AdministratorsManage: 'Operations.AdminAccess.Administrators.Manage',
  AdministratorsRead: 'Operations.AdminAccess.Administrators.Read',
  AdministratorsResetPassword:
    'Operations.AdminAccess.Administrators.ResetPassword',
  AuditLogsRead: 'Operations.AdminAccess.AuditLogs.Read',
  RolesManage: 'Operations.AdminAccess.Roles.Manage',
  RolesRead: 'Operations.AdminAccess.Roles.Read',
  SessionsRead: 'Operations.AdminAccess.Sessions.Read',
  SessionsRevoke: 'Operations.AdminAccess.Sessions.Revoke',
} as const;

export const AdminAccessErrorCode = {
  AdministratorNotFound: 'NovaGame.AdminAccess.AdministratorNotFound',
  RoleNotFound: 'NovaGame.AdminAccess.RoleNotFound',
  SessionFamilyNotFound: 'NovaGame.AdminAccess.SessionFamilyNotFound',
} as const;

export type AdminAccessPermissionCode =
  (typeof AdminAccessPermission)[keyof typeof AdminAccessPermission];

export type AdminAccessLeafPermission = Exclude<
  AdminAccessPermissionCode,
  typeof AdminAccessPermission.Access
>;

export function hasAllAdminAccessPermissions(
  accessCodes: readonly string[],
  requiredPermissions: readonly AdminAccessPermissionCode[],
): boolean {
  const grantedPermissions = new Set(accessCodes);
  return requiredPermissions.every((permission) =>
    grantedPermissions.has(permission),
  );
}

export function hasAdminAccessPermission(
  accessCodes: readonly string[],
  leafPermission: AdminAccessLeafPermission,
): boolean {
  return hasAllAdminAccessPermissions(accessCodes, [
    AdminAccessPermission.Access,
    leafPermission,
  ]);
}
