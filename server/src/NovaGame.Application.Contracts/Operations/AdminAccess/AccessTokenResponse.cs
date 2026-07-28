namespace NovaGame.Application.Contracts.Operations.AdminAccess;

public sealed record AccessTokenResponse(
    string AccessToken,
    string TokenType,
    DateTime ExpiresAtUtc);
