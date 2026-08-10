using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using NovaCore.BuildingBlocks.MultiTenancy;
using SwordMaster.Host.EntityFrameworkCore.DependencyInjection;

namespace SwordMaster.Host.EntityFrameworkCore;

public sealed class SwordMasterDbContextFactory : IDesignTimeDbContextFactory<SwordMasterDbContext>
{
    public SwordMasterDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("SWORDMASTER_MYSQL_MIGRATION_CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__SwordMaster")
            ?? throw new InvalidOperationException(
                "Set SWORDMASTER_MYSQL_MIGRATION_CONNECTION_STRING before running EF migration commands.");

        var options = new DbContextOptionsBuilder<SwordMasterDbContext>();
        SwordMasterEntityFrameworkCoreServiceCollectionExtensions.ConfigureMySql(
            options,
            connectionString,
            "8.4.0");

        return new SwordMasterDbContext(options.Options, new CurrentTenant());
    }
}
