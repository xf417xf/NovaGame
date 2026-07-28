namespace NovaGame.Domain.Operations.AdminAccess;

public enum AdminAccessAuditAction
{
    Login = 1,
    Refresh = 2,
    Logout = 3,
    RefreshTokenReuseDetected = 4,
    AdministratorCreated = 5,
    RoleCreated = 6,
    RoleAssigned = 7,
    PermissionGranted = 8,
    PermissionRevoked = 9,
    PasswordRehashed = 10
}
