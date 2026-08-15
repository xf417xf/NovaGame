namespace NovaGame.Application.Contracts.Operations.AdminAccess;

public static class AdminAccessManagementContractLimits
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaximumPageSize = 100;
    public const int SearchMaximumLength = 128;
    public const int CorrelationIdMaximumLength = 128;
    public const int DisplayNameMaximumLength = 128;
    public const int RoleNameMaximumLength = 64;
    public const int PasswordMinimumLength = 12;
    public const int PasswordMaximumLength = 256;
    public const int MaximumRoleIds = 100;
    public const int MaximumPermissionNames = 512;
    public const int PermissionNameMaximumLength = 128;
    public const int MaximumAuditRangeDays = 31;
}
