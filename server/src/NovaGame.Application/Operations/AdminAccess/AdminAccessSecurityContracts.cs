namespace NovaGame.Application.Operations.AdminAccess;

public interface IPasswordHashingService
{
    string Hash(string password);

    PasswordHashVerificationResult Verify(string? passwordHash, string password);
}

public enum PasswordHashVerificationResult
{
    Failed = 0,
    Success = 1,
    SuccessRehashNeeded = 2
}

public interface IAccessTokenIssuer
{
    string Issue(
        Guid administratorId,
        string userName,
        DateTime issuedAtUtc,
        DateTime expiresAtUtc);
}

public interface IRefreshTokenService
{
    IssuedRefreshToken Create();

    bool TryHash(string? token, out byte[] tokenHash);
}

public sealed record IssuedRefreshToken(string Value, byte[] Hash);
