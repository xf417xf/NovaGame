using Microsoft.EntityFrameworkCore;
using NovaCore.BuildingBlocks.MultiTenancy;
using SwordMaster.Host.EntityFrameworkCore;
using SwordMaster.Host.EntityFrameworkCore.DependencyInjection;
using Xunit;

namespace SwordMaster.Integration.Tests.PlayerAssets;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SwordMasterMySqlCollection : ICollectionFixture<SwordMasterMySqlDatabaseFixture>
{
    public const string Name = "SwordMaster MySQL database";
}

public sealed class SwordMasterMySqlDatabaseFixture : IAsyncLifetime
{
    public const string InitialMigrationId = "20260801121309_InitialPlayerAssets";

    public SwordMasterMySqlDatabaseFixture()
    {
        ConnectionString = Environment.GetEnvironmentVariable(
            "SWORDMASTER_TEST_MYSQL_CONNECTION_STRING")
            ?? throw new InvalidOperationException(
                "SWORDMASTER_TEST_MYSQL_CONNECTION_STRING is required for SwordMaster MySQL integration tests.");
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

    public SwordMasterDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<SwordMasterDbContext>();
        SwordMasterEntityFrameworkCoreServiceCollectionExtensions.ConfigureMySql(
            options,
            ConnectionString,
            "8.4.0");

        return new SwordMasterDbContext(options.Options, new CurrentTenant());
    }
}
