using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using NovaCore.BuildingBlocks.MultiTenancy;
using NovaGame.Host.EntityFrameworkCore.DependencyInjection;

namespace NovaGame.Host.EntityFrameworkCore;

public sealed class NovaGameDbContextFactory : IDesignTimeDbContextFactory<NovaGameDbContext>
{
    public NovaGameDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("NOVAGAME_MYSQL_MIGRATION_CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? throw new InvalidOperationException(
                "Set NOVAGAME_MYSQL_MIGRATION_CONNECTION_STRING before running EF migration commands.");

        var options = new DbContextOptionsBuilder<NovaGameDbContext>();
        NovaGameEntityFrameworkCoreServiceCollectionExtensions.ConfigureMySql(
            options,
            connectionString,
            "8.4.0");

        return new NovaGameDbContext(options.Options, new CurrentTenant());
    }
}
