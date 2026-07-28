using Microsoft.Extensions.Configuration;
using NovaGame.Api.Operations.AdminAccess;
using NovaGame.Application.Operations.AdminAccess;
using NovaGame.Deployment;
using Xunit;

namespace NovaGame.EntityFrameworkCore.Tests;

public sealed class AdminAccessConfigurationTests
{
    [Fact]
    public void JwtOptionsAcceptBase64UrlAndRejectOrdinaryBase64()
    {
        var bytes = Enumerable.Repeat((byte)251, 32).ToArray();
        var ordinaryBase64 = Convert.ToBase64String(bytes);
        var base64Url = ordinaryBase64.TrimEnd('=').Replace('+', '-').Replace('/', '_');

        Assert.False(AdminAccessJwtOptions.IsValid(CreateJwtOptions(ordinaryBase64)));
        Assert.True(AdminAccessJwtOptions.IsValid(CreateJwtOptions(base64Url)));
    }

    [Fact]
    public void ProductionValidationRejectsOrdinaryBase64SigningKeys()
    {
        var ordinaryBase64 = Convert.ToBase64String(Enumerable.Repeat((byte)251, 32).ToArray());
        var configuration = CreateProductionConfiguration(ordinaryBase64);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            NovaGameProductionConfigurationValidator.Validate(configuration, "Production"));

        Assert.Contains("SigningKey", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionValidationAcceptsBase64UrlSigningKeys()
    {
        var base64Url = Convert.ToBase64String(Enumerable.Repeat((byte)251, 32).ToArray())
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        NovaGameProductionConfigurationValidator.Validate(
            CreateProductionConfiguration(base64Url),
            "Production");
    }

    [Fact]
    public void RefreshTokensRequireCanonicalUnpaddedBase64Url()
    {
        var bytes = Enumerable.Repeat((byte)251, 32).ToArray();
        var ordinaryBase64 = Convert.ToBase64String(bytes);
        var base64Url = ordinaryBase64.TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var service = new RefreshTokenService();

        Assert.False(service.TryHash(ordinaryBase64, out _));
        Assert.False(service.TryHash(base64Url + "=", out _));
        Assert.True(service.TryHash(base64Url, out var tokenHash));
        Assert.Equal(System.Security.Cryptography.SHA256.HashData(bytes), tokenHash);
    }

    private static AdminAccessJwtOptions CreateJwtOptions(string signingKey)
    {
        return new AdminAccessJwtOptions
        {
            Issuer = "NovaGame.Tests",
            Audience = "NovaGame.Admin.Tests",
            SigningKey = signingKey,
            ClockSkew = TimeSpan.Zero
        };
    }

    private static IConfiguration CreateProductionConfiguration(string signingKey)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Server=127.0.0.1;Database=novagame;User ID=novagame;Password=unused",
                ["NovaGame:EntityFrameworkCore:ConnectionStringName"] = "Default",
                ["NovaGame:EntityFrameworkCore:ServerVersion"] = "8.4.0",
                ["NovaGame:AdminAccess:Jwt:Issuer"] = "NovaGame.Tests",
                ["NovaGame:AdminAccess:Jwt:Audience"] = "NovaGame.Admin.Tests",
                ["NovaGame:AdminAccess:Jwt:SigningKey"] = signingKey,
                ["NovaGame:AdminAccess:Jwt:ClockSkew"] = "00:00:30",
                ["AllowedHosts"] = "localhost"
            })
            .Build();
    }
}
