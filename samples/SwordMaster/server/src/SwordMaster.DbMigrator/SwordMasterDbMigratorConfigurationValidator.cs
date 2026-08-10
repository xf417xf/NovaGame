using Microsoft.Extensions.Configuration;
using NovaGame.Application.Operations.AdminAccess;
using SwordMaster.Host.EntityFrameworkCore.DependencyInjection;

namespace SwordMaster.DbMigrator;

public static class SwordMasterDbMigratorConfigurationValidator
{
    private const int MinimumSeedPasswordLength = 12;
    private const int MaximumSeedPasswordLength = 256;

    public static void Validate(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        SwordMasterDatabaseConfigurationValidator.Validate(configuration);

        var seedSection = configuration.GetSection(AdminAccessSeedOptions.SectionName);
        var userName = seedSection[nameof(AdminAccessSeedOptions.UserName)];
        var password = seedSection[nameof(AdminAccessSeedOptions.Password)];
        var hasUserName = !string.IsNullOrWhiteSpace(userName);
        var hasPassword = !string.IsNullOrWhiteSpace(password);

        if (hasUserName != hasPassword)
        {
            throw new InvalidOperationException(
                "Administrator seed configuration requires both a user name and a password.");
        }

        if (hasPassword && password!.Length is < MinimumSeedPasswordLength or > MaximumSeedPasswordLength)
        {
            throw new InvalidOperationException(
                $"Administrator seed passwords must contain {MinimumSeedPasswordLength} to {MaximumSeedPasswordLength} characters.");
        }
    }
}
