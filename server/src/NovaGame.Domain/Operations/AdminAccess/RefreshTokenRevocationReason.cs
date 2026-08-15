namespace NovaGame.Domain.Operations.AdminAccess;

public enum RefreshTokenRevocationReason
{
    Rotated = 1,
    Logout = 2,
    ReuseDetected = 3,
    Expired = 4,
    AdministratorInactive = 5,
    PasswordReset = 6,
    ForcedByAdministrator = 7
}
