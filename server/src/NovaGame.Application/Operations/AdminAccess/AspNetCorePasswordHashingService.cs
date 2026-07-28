using Microsoft.AspNetCore.Identity;

namespace NovaGame.Application.Operations.AdminAccess;

public sealed class AspNetCorePasswordHashingService : IPasswordHashingService
{
    private static readonly object PasswordSubject = new();

    private readonly PasswordHasher<object> _passwordHasher = new();
    private readonly string _dummyPasswordHash;

    public AspNetCorePasswordHashingService()
    {
        _dummyPasswordHash = _passwordHasher.HashPassword(
            PasswordSubject,
            "NovaGame.Invalid.Administrator.Password");
    }

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        return _passwordHasher.HashPassword(PasswordSubject, password);
    }

    public PasswordHashVerificationResult Verify(string? passwordHash, string password)
    {
        var result = _passwordHasher.VerifyHashedPassword(
            PasswordSubject,
            passwordHash ?? _dummyPasswordHash,
            password);

        return result switch
        {
            Microsoft.AspNetCore.Identity.PasswordVerificationResult.Success =>
                PasswordHashVerificationResult.Success,
            Microsoft.AspNetCore.Identity.PasswordVerificationResult.SuccessRehashNeeded =>
                PasswordHashVerificationResult.SuccessRehashNeeded,
            _ => PasswordHashVerificationResult.Failed
        };
    }
}
