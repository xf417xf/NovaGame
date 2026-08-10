using Microsoft.Extensions.Configuration;
using SwordMaster.DbMigrator;
using Xunit;

namespace SwordMaster.Integration.Tests.RuntimeComposition;

[Collection(SwordMasterRuntimeCompositionCollection.Name)]
public sealed class SwordMasterDbMigratorConfigurationTests
{
    [Fact]
    public void DbMigratorScopedValidationAcceptsBlankHttpConfiguration()
    {
        var configuration = CreateConfiguration();

        var exception = Record.Exception(
            () => SwordMasterDbMigratorConfigurationValidator.Validate(configuration));

        Assert.Null(exception);
    }

    [Theory]
    [InlineData("runtime-admin", "")]
    [InlineData("", "a-valid-seed-password")]
    public void DbMigratorScopedValidationRejectsPartialAdministratorSeedCredentials(
        string userName,
        string password)
    {
        var configuration = CreateConfiguration(
            new Dictionary<string, string?>
            {
                ["NovaGame:AdminAccess:Seed:UserName"] = userName,
                ["NovaGame:AdminAccess:Seed:Password"] = password
            });

        var exception = Assert.Throws<InvalidOperationException>(
            () => SwordMasterDbMigratorConfigurationValidator.Validate(configuration));

        Assert.Equal(
            "Administrator seed configuration requires both a user name and a password.",
            exception.Message);
    }

    [Theory]
    [InlineData(11)]
    [InlineData(257)]
    public void DbMigratorScopedValidationRejectsAdministratorSeedPasswordsOutsideBounds(
        int passwordLength)
    {
        var configuration = CreateConfiguration(
            new Dictionary<string, string?>
            {
                ["NovaGame:AdminAccess:Seed:UserName"] = "runtime-admin",
                ["NovaGame:AdminAccess:Seed:Password"] = new string('a', passwordLength)
            });

        var exception = Assert.Throws<InvalidOperationException>(
            () => SwordMasterDbMigratorConfigurationValidator.Validate(configuration));

        Assert.Equal(
            "Administrator seed passwords must contain 12 to 256 characters.",
            exception.Message);
    }

    private static IConfiguration CreateConfiguration(
        IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:NovaGame"] =
                "Server=127.0.0.1;Port=3306;Database=novagame;User ID=unused;Password=unused;",
            ["ConnectionStrings:SwordMaster"] =
                "Server=localhost;Port=3306;Database=swordmaster;User ID=unused;Password=unused;",
            ["NovaGame:EntityFrameworkCore:ConnectionStringName"] = "NovaGame",
            ["NovaGame:EntityFrameworkCore:ServerVersion"] = "8.4.0",
            ["SwordMaster:EntityFrameworkCore:ConnectionStringName"] = "SwordMaster",
            ["SwordMaster:EntityFrameworkCore:ServerVersion"] = "8.4.0",
            ["NovaGame:AdminAccess:Seed:UserName"] = "",
            ["NovaGame:AdminAccess:Seed:Password"] = "",
            ["NovaGame:AdminAccess:Jwt:Issuer"] = "",
            ["NovaGame:AdminAccess:Jwt:Audience"] = "",
            ["NovaGame:AdminAccess:Jwt:SigningKey"] = "",
            ["AllowedHosts"] = ""
        };

        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
            {
                settings[key] = value;
            }
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
    }
}
