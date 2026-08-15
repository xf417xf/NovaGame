using NovaCore.BuildingBlocks.Permissions;

namespace NovaGame.Domain.Shared.Operations.AdminAccess;

public sealed class AdminAccessPermissionDefinitionProvider : IPermissionDefinitionProvider
{
    public void Define(PermissionDefinitionContext context)
    {
        var group = context.GetOrAddGroup(AdminAccessPermissions.GroupName, "Administrator access");
        group.AddPermission(
            AdminAccessPermissions.Access,
            "Access operations administration",
            "Allows an administrator to use authenticated operations APIs.");
        group.AddPermission(
            AdminAccessPermissions.AdministratorsRead,
            "Read administrators",
            "Allows an administrator to list and inspect administrator accounts.");
        group.AddPermission(
            AdminAccessPermissions.AdministratorsManage,
            "Manage administrators",
            "Allows an administrator to create and manage administrator accounts.");
        group.AddPermission(
            AdminAccessPermissions.AdministratorsResetPassword,
            "Reset administrator passwords",
            "Allows an administrator to reset another administrator's password.");
        group.AddPermission(
            AdminAccessPermissions.RolesRead,
            "Read administrator roles",
            "Allows an administrator to list and inspect roles and registered permissions.");
        group.AddPermission(
            AdminAccessPermissions.RolesManage,
            "Manage administrator roles",
            "Allows an administrator to create and manage custom administrator roles.");
        group.AddPermission(
            AdminAccessPermissions.SessionsRead,
            "Read administrator sessions",
            "Allows an administrator to list redacted administrator refresh-token families.");
        group.AddPermission(
            AdminAccessPermissions.SessionsRevoke,
            "Revoke administrator sessions",
            "Allows an administrator to revoke an administrator refresh-token family.");
        group.AddPermission(
            AdminAccessPermissions.AuditLogsRead,
            "Read administrator access audit logs",
            "Allows an administrator to query administrator-access audit history.");
    }
}
