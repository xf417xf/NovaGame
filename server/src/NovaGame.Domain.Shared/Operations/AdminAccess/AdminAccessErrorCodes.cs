namespace NovaGame.Domain.Shared.Operations.AdminAccess;

public static class AdminAccessErrorCodes
{
    public const string InvalidCredentials = "NovaGame.AdminAccess.InvalidCredentials";
    public const string AccountInactive = "NovaGame.AdminAccess.AccountInactive";
    public const string RefreshTokenRequired = "NovaGame.AdminAccess.RefreshTokenRequired";
    public const string RefreshTokenInvalid = "NovaGame.AdminAccess.RefreshTokenInvalid";
    public const string RefreshTokenExpired = "NovaGame.AdminAccess.RefreshTokenExpired";
    public const string RefreshTokenReused = "NovaGame.AdminAccess.RefreshTokenReused";
    public const string Unauthorized = "NovaGame.AdminAccess.Unauthorized";
    public const string Forbidden = "NovaGame.AdminAccess.Forbidden";
    public const string LoginRateLimited = "NovaGame.AdminAccess.LoginRateLimited";
}
