using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
    public const string PlayerEquipmentGrantMigrationId =
        "20260810105902_AddPlayerEquipmentGrants";

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

    public SwordMasterDbContext CreateDbContext(params IInterceptor[] interceptors)
    {
        ArgumentNullException.ThrowIfNull(interceptors);
        var options = new DbContextOptionsBuilder<SwordMasterDbContext>();
        SwordMasterEntityFrameworkCoreServiceCollectionExtensions.ConfigureMySql(
            options,
            ConnectionString,
            "8.4.0");
        if (interceptors.Length > 0)
        {
            options.AddInterceptors(interceptors);
        }

        return new SwordMasterDbContext(options.Options, new CurrentTenant());
    }
}
