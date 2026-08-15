namespace NovaGame.Application.Contracts.Operations.AdminAccess;

public enum AdminSessionFamilyState
{
    Active = 1,
    Expired = 2,
    Revoked = 3
}

public enum AdminSessionRevocationReason
{
    Rotated = 1,
    Logout = 2,
    ReuseDetected = 3,
    Expired = 4,
    AdministratorInactive = 5,
    PasswordReset = 6,
    ForcedByAdministrator = 7
}

public enum AdminAccessAuditActionDto
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
