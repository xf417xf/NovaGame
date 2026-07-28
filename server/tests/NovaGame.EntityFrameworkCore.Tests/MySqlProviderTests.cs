using Microsoft.EntityFrameworkCore;
using NovaCore.BuildingBlocks.UnitOfWork;
using NovaCore.Framework.EntityFrameworkCore.UnitOfWork;
using Xunit;

namespace NovaGame.EntityFrameworkCore.Tests;

[Collection(MySqlDatabaseCollection.Name)]
public sealed class MySqlProviderTests
{
    private readonly MySqlDatabaseFixture _fixture;

    public MySqlProviderTests(MySqlDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task UsesPomeloAndAppliesTheBaselineMigration()
    {
        await using var dbContext = _fixture.CreateDbContext();

        Assert.Equal("Pomelo.EntityFrameworkCore.MySql", dbContext.Database.ProviderName);
        Assert.True(await dbContext.Database.CanConnectAsync());

        var appliedMigrations = await dbContext.Database.GetAppliedMigrationsAsync();
        Assert.Contains(MySqlDatabaseFixture.InitialMigrationId, appliedMigrations);
        Assert.Contains(MySqlDatabaseFixture.AdministratorAccessMigrationId, appliedMigrations);
        Assert.Contains(MySqlDatabaseFixture.GameConfigMigrationId, appliedMigrations);
        Assert.False(dbContext.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task NovaCoreTransactionalUnitOfWorkIsCompatibleWithMySql()
    {
        await using var dbContext = _fixture.CreateDbContext();
        await using var unitOfWork = new EfCoreUnitOfWork([dbContext]);

        await unitOfWork.BeginAsync(new UnitOfWorkOptions());
        await unitOfWork.CompleteAsync();

        Assert.True(unitOfWork.IsCompleted);
    }

    [Fact]
    public async Task UsesMySql84Utf8Mb4AndUtc()
    {
        await using var dbContext = _fixture.CreateDbContext();
        var connection = dbContext.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT VERSION(), @@session.time_zone, @@character_set_database";

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.StartsWith("8.4.", reader.GetString(0));
        Assert.Equal("+00:00", reader.GetString(1));
        Assert.Equal("utf8mb4", reader.GetString(2));
    }
}
