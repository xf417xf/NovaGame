using System.Security.Cryptography;

namespace NovaGame.Application.Operations.AdminAccess;

public sealed class RefreshTokenService : IRefreshTokenService
{
    private const int TokenByteLength = 32;
    private const int EncodedTokenLength = 43;

    public IssuedRefreshToken Create()
    {
        var bytes = RandomNumberGenerator.GetBytes(TokenByteLength);
        var token = Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        return new IssuedRefreshToken(token, SHA256.HashData(bytes));
    }

    public bool TryHash(string? token, out byte[] tokenHash)
    {
        if (string.IsNullOrWhiteSpace(token) ||
            token.Length != EncodedTokenLength ||
            token.Any(character =>
                !char.IsAsciiLetterOrDigit(character) &&
                character is not '-' and not '_'))
        {
            tokenHash = [];
            return false;
        }

        try
        {
            var base64 = token.Replace('-', '+').Replace('_', '/');
            var paddingLength = (4 - (base64.Length % 4)) % 4;
            if (paddingLength > 0)
            {
                base64 += new string('=', paddingLength);
            }

            var tokenBytes = Convert.FromBase64String(base64);
            if (tokenBytes.Length != TokenByteLength)
            {
                tokenHash = [];
                return false;
            }

            tokenHash = SHA256.HashData(tokenBytes);
            return true;
        }
        catch (FormatException)
        {
            tokenHash = [];
            return false;
        }
    }
}
