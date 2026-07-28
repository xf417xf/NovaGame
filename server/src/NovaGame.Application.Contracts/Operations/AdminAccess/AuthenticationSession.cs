namespace NovaGame.Application.Contracts.Operations.AdminAccess;

public sealed record AuthenticationSession(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc)
{
    public AccessTokenResponse ToResponse()
    {
        return new AccessTokenResponse(AccessToken, "Bearer", AccessTokenExpiresAtUtc);
    }
}
