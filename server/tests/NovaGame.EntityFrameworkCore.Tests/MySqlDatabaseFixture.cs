using Microsoft.EntityFrameworkCore;
using NovaCore.BuildingBlocks.MultiTenancy;
using NovaGame.Host.EntityFrameworkCore;
using NovaGame.Host.EntityFrameworkCore.DependencyInjection;
using Xunit;

namespace NovaGame.EntityFrameworkCore.Tests;

[CollectionDefinition(Name)]
public sealed class MySqlDatabaseCollection : ICollectionFixture<MySqlDatabaseFixture>
{
    public const string Name = "MySQL database";
}

public sealed class MySqlDatabaseFixture : IAsyncLifetime
{
    public const string InitialMigrationId = "20260723160000_InitialMySqlBaseline";
    public const string AdministratorAccessMigrationId = "20260725101129_AddAdministratorAccess";
    public const string GameConfigMigrationId = "20260725122154_AddGameConfig";
    public const string AdminAccessManagementMigrationId = "20260815030331_AddAdminAccessManagement";

    public MySqlDatabaseFixture()
    {
        ConnectionString = Environment.GetEnvironmentVariable("NOVAGAME_TEST_MYSQL_CONNECTION_STRING")
            ?? throw new InvalidOperationException(
                "NOVAGAME_TEST_MYSQL_CONNECTION_STRING is required for MySQL integration tests.");
    }

    public string ConnectionString { get; }

    public async Task InitializeAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    public NovaGameDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<NovaGameDbContext>();
        NovaGameEntityFrameworkCoreServiceCollectionExtensions.ConfigureMySql(
            options,
            ConnectionString,
            "8.4.0");

        return new NovaGameDbContext(options.Options, new CurrentTenant());
    }
}
