namespace NovaGame.Domain.Shared.Operations.AdminAccess;

public static class AdminAccessPermissions
{
    public const string GroupName = "Operations.AdminAccess";
    public const string Access = GroupName + ".Access";
    public const string AdministratorsRead = GroupName + ".Administrators.Read";
    public const string AdministratorsManage = GroupName + ".Administrators.Manage";
    public const string AdministratorsResetPassword = GroupName + ".Administrators.ResetPassword";
    public const string RolesRead = GroupName + ".Roles.Read";
    public const string RolesManage = GroupName + ".Roles.Manage";
    public const string SessionsRead = GroupName + ".Sessions.Read";
    public const string SessionsRevoke = GroupName + ".Sessions.Revoke";
    public const string AuditLogsRead = GroupName + ".AuditLogs.Read";
}
