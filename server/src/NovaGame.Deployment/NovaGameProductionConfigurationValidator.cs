using Microsoft.Extensions.Configuration;

namespace NovaGame.Deployment;

public static class NovaGameProductionConfigurationValidator
{
    private const string ProductionEnvironmentName = "Production";
    private const string EntityFrameworkCoreSectionName = "NovaGame:EntityFrameworkCore";
    private const string JwtSectionName = "NovaGame:AdminAccess:Jwt";
    private const string DefaultConnectionStringName = "Default";

    public static void Validate(IConfiguration configuration, string environmentName)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (!environmentName.Equals(ProductionEnvironmentName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var errors = new List<string>();
        ValidateDatabase(configuration, errors);
        ValidateAuthentication(configuration, errors);
        ValidateAllowedHosts(configuration, errors);

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Production configuration validation failed:" + Environment.NewLine +
                string.Join(Environment.NewLine, errors.Select(error => $"- {error}")));
        }
    }

    private static void ValidateAuthentication(
        IConfiguration configuration,
        ICollection<string> errors)
    {
        var section = configuration.GetSection(JwtSectionName);
        if (string.IsNullOrWhiteSpace(section["Issuer"]))
        {
            errors.Add($"{JwtSectionName}:Issuer is required.");
        }
        if (string.IsNullOrWhiteSpace(section["Audience"]))
        {
            errors.Add($"{JwtSectionName}:Audience is required.");
        }
        if (!TryDecodeSigningKey(section["SigningKey"], out var keyBytes) || keyBytes.Length < 32)
        {
            errors.Add($"{JwtSectionName}:SigningKey must be base64url and contain at least 32 bytes.");
        }

        if (!TimeSpan.TryParse(section["ClockSkew"], out var clockSkew) ||
            clockSkew < TimeSpan.Zero ||
            clockSkew > TimeSpan.FromMinutes(5))
        {
            errors.Add($"{JwtSectionName}:ClockSkew must be between zero and five minutes.");
        }
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
            return true;
        }
        catch (FormatException)
        {
            keyBytes = [];
            return false;
        }
    }

    private static void ValidateDatabase(IConfiguration configuration, ICollection<string> errors)
    {
        var section = configuration.GetSection(EntityFrameworkCoreSectionName);
        var connectionStringName = section["ConnectionStringName"] ?? DefaultConnectionStringName;
        var serverVersion = section["ServerVersion"];

        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString(connectionStringName)))
        {
            errors.Add($"ConnectionStrings:{connectionStringName} must be supplied by the deployment.");
        }

        if (!Version.TryParse(serverVersion, out var parsedServerVersion) ||
            parsedServerVersion.Major != 8 ||
            parsedServerVersion.Minor != 4)
        {
            errors.Add($"{EntityFrameworkCoreSectionName}:ServerVersion must target MySQL 8.4.x.");
        }
    }

    private static void ValidateAllowedHosts(IConfiguration configuration, ICollection<string> errors)
    {
        var allowedHosts = configuration["AllowedHosts"]?
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (allowedHosts is null || allowedHosts.Length == 0 || allowedHosts.Contains("*", StringComparer.Ordinal))
        {
            errors.Add("AllowedHosts must contain explicit host names and must not use '*'.");
        }
    }
}
