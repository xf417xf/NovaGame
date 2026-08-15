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
    PasswordRehashed = 10,
    AdministratorDisplayNameChanged = 11,
    AdministratorActivated = 12,
    AdministratorDeactivated = 13,
    AdministratorPasswordReset = 14,
    RoleRemoved = 15,
    RoleRenamed = 16,
    RoleActivated = 17,
    RoleDeactivated = 18,
    SessionFamilyRevoked = 19
}
