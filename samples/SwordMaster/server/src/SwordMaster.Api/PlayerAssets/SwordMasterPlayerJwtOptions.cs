namespace SwordMaster.Api.PlayerAssets;

public sealed class SwordMasterPlayerJwtOptions
{
    public const string SectionName = "SwordMaster:PlayerAuthentication:Jwt";

    public string Issuer { get; set; } = "SwordMaster";

    public string Audience { get; set; } = "SwordMaster.Player";

    public string SigningKey { get; set; } = string.Empty;

    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromSeconds(30);

    public static bool IsValid(SwordMasterPlayerJwtOptions options)
    {
        return !string.IsNullOrWhiteSpace(options.Issuer) &&
            !string.IsNullOrWhiteSpace(options.Audience) &&
            TryDecodeSigningKey(options.SigningKey, out _) &&
            options.ClockSkew >= TimeSpan.Zero &&
            options.ClockSkew <= TimeSpan.FromMinutes(5);
    }

    public byte[] GetSigningKeyBytes()
    {
        if (!TryDecodeSigningKey(SigningKey, out var keyBytes))
        {
            throw new InvalidOperationException(
                $"{SectionName}:SigningKey is not a valid base64url key containing at least 32 bytes.");
        }

        return keyBytes;
    }

    private static bool TryDecodeSigningKey(string? value, out byte[] keyBytes)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Any(character =>
                !char.IsAsciiLetterOrDigit(character) &&
                character is not '-' and not '_'))
        {
            keyBytes = [];
            return false;
        }

        try
        {
            var base64 = value.Replace('-', '+').Replace('_', '/');
            var paddingLength = (4 - (base64.Length % 4)) % 4;
            if (paddingLength > 0)
            {
                base64 += new string('=', paddingLength);
            }

            keyBytes = Convert.FromBase64String(base64);
            return keyBytes.Length >= 32;
        }
        catch (FormatException)
        {
            keyBytes = [];
            return false;
        }
    }
}
