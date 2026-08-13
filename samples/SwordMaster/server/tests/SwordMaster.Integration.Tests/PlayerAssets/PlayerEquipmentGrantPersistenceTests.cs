using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using SwordMaster.Domain.PlayerAssets;
using SwordMaster.Host.EntityFrameworkCore;
using SwordMaster.Host.EntityFrameworkCore.PlayerAssets;
using Xunit;

namespace SwordMaster.Integration.Tests.PlayerAssets;

[Collection(SwordMasterMySqlCollection.Name)]
public sealed class PlayerEquipmentGrantPersistenceTests
{
    private const string GrantFingerprint =
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string RollbackFingerprint =
        "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

    private static readonly DateTime GrantedAtUtc =
        new(2026, 8, 10, 1, 2, 3, 123, DateTimeKind.Utc);

    private readonly SwordMasterMySqlDatabaseFixture _fixture;

    public PlayerEquipmentGrantPersistenceTests(SwordMasterMySqlDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task MigrationCreatesExpectedPlayerEquipmentGrantSchema()
    {
        await using var dbContext = _fixture.CreateDbContext();
        Assert.Contains(
            SwordMasterMySqlDatabaseFixture.PlayerEquipmentGrantMigrationId,
            await dbContext.Database.GetAppliedMigrationsAsync());
        Assert.False(dbContext.Database.HasPendingModelChanges());

        var connection = dbContext.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var columnCommand = connection.CreateCommand();
        columnCommand.CommandText = """
            SELECT COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE
            FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'sm_player_equipment_grants'
            ORDER BY ORDINAL_POSITION
            """;
        var columns = new List<ColumnSchema>();
        await using (var reader = await columnCommand.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                columns.Add(new ColumnSchema(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2)));
            }
        }

        Assert.Equal(
            [
                new("GrantId", "char(36)", "NO"),
                new("PlayerId", "char(36)", "NO"),
                new("OwnedItemId", "char(36)", "NO"),
                new("ConfiguredItemId", "varchar(128)", "NO"),
                new("ConfigurationVersionId", "char(36)", "NO"),
                new("Status", "tinyint unsigned", "NO"),
                new("GrantedByAdministratorId", "char(36)", "NO"),
                new("GrantedAtUtc", "datetime(6)", "NO"),
                new("GrantReason", "varchar(500)", "NO"),
                new("GrantIdempotencyKey", "char(36)", "NO"),
                new("GrantRequestFingerprint", "char(64)", "NO"),
                new("GrantResultingRevision", "bigint", "NO"),
                new("GrantIpAddress", "varchar(64)", "YES"),
                new("GrantUserAgent", "varchar(512)", "YES"),
                new("GrantCorrelationId", "varchar(128)", "YES"),
                new("RolledBackByAdministratorId", "char(36)", "YES"),
                new("RolledBackAtUtc", "datetime(6)", "YES"),
                new("RollbackReason", "varchar(500)", "YES"),
                new("RollbackIdempotencyKey", "char(36)", "YES"),
                new("RollbackRequestFingerprint", "char(64)", "YES"),
                new("RollbackResultingRevision", "bigint", "YES"),
                new("RollbackIpAddress", "varchar(64)", "YES"),
                new("RollbackUserAgent", "varchar(512)", "YES"),
                new("RollbackCorrelationId", "varchar(128)", "YES")
            ],
            columns);

        await using var collationCommand = connection.CreateCommand();
        collationCommand.CommandText = """
            SELECT COLUMN_NAME, CHARACTER_SET_NAME, COLLATION_NAME
            FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'sm_player_equipment_grants'
              AND COLUMN_NAME IN (
                'ConfiguredItemId',
                'GrantRequestFingerprint',
                'RollbackRequestFingerprint')
            ORDER BY COLUMN_NAME
            """;
        var collations = new List<string>();
        await using (var reader = await collationCommand.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                collations.Add($"{reader.GetString(0)}:{reader.GetString(1)}:{reader.GetString(2)}");
            }
        }

        Assert.Equal(
            [
                "ConfiguredItemId:utf8mb4:utf8mb4_0900_bin",
                "GrantRequestFingerprint:ascii:ascii_bin",
                "RollbackRequestFingerprint:ascii:ascii_bin"
            ],
            collations);

        await using var indexCommand = connection.CreateCommand();
        indexCommand.CommandText = """
            SELECT INDEX_NAME, NON_UNIQUE, SEQ_IN_INDEX, COLUMN_NAME
            FROM information_schema.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'sm_player_equipment_grants'
            """;
        var indexes = new List<IndexColumn>();
        await using (var reader = await indexCommand.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                indexes.Add(new IndexColumn(
                    reader.GetString(0),
                    reader.GetInt64(1) == 0,
                    reader.GetInt32(2),
                    reader.GetString(3)));
            }
        }

        Assert.Equal(
            ExpectedIndexes().OrderBy(IndexSortKey, StringComparer.Ordinal),
            indexes.OrderBy(IndexSortKey, StringComparer.Ordinal));

        await using var foreignKeyCommand = connection.CreateCommand();
        foreignKeyCommand.CommandText = """
            SELECT k.CONSTRAINT_NAME, k.COLUMN_NAME, k.REFERENCED_TABLE_NAME,
                   k.REFERENCED_COLUMN_NAME, r.DELETE_RULE
            FROM information_schema.KEY_COLUMN_USAGE AS k
            INNER JOIN information_schema.REFERENTIAL_CONSTRAINTS AS r
              ON r.CONSTRAINT_SCHEMA = k.CONSTRAINT_SCHEMA
             AND r.CONSTRAINT_NAME = k.CONSTRAINT_NAME
            WHERE k.TABLE_SCHEMA = DATABASE()
              AND k.TABLE_NAME = 'sm_player_equipment_grants'
              AND k.REFERENCED_TABLE_NAME IS NOT NULL
            """;
        await using (var reader = await foreignKeyCommand.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.Equal("fk_sm_player_equipment_grants_player", reader.GetString(0));
            Assert.Equal("PlayerId", reader.GetString(1));
            Assert.Equal("sm_player_assets_profiles", reader.GetString(2));
            Assert.Equal("PlayerId", reader.GetString(3));
            Assert.Equal("RESTRICT", reader.GetString(4));
            Assert.False(await reader.ReadAsync());
        }

        await using var checkCommand = connection.CreateCommand();
        checkCommand.CommandText = """
            SELECT t.CONSTRAINT_NAME, c.CHECK_CLAUSE, t.ENFORCED
            FROM information_schema.TABLE_CONSTRAINTS AS t
            INNER JOIN information_schema.CHECK_CONSTRAINTS AS c
              ON c.CONSTRAINT_SCHEMA = t.CONSTRAINT_SCHEMA
             AND c.CONSTRAINT_NAME = t.CONSTRAINT_NAME
            WHERE t.TABLE_SCHEMA = DATABASE()
              AND t.TABLE_NAME = 'sm_player_equipment_grants'
              AND t.CONSTRAINT_TYPE = 'CHECK'
            ORDER BY t.CONSTRAINT_NAME
            """;
        var checks = new List<CheckConstraint>();
        await using (var reader = await checkCommand.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                checks.Add(new CheckConstraint(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2)));
            }
        }

        Assert.Equal(
            [
                "ck_sm_player_equipment_grants_grant_revision",
                "ck_sm_player_equipment_grants_rollback_complete",
                "ck_sm_player_equipment_grants_rollback_revision",
                "ck_sm_player_equipment_grants_rollback_time",
                "ck_sm_player_equipment_grants_status"
            ],
            checks.Select(check => check.Name));
        Assert.All(checks, check =>
        {
            Assert.Equal("YES", check.Enforced);
            Assert.False(string.IsNullOrWhiteSpace(check.Clause));
        });
        AssertCheckClauseContains(checks, "grant_revision", "GrantResultingRevision", ">");
        AssertCheckClauseContains(
            checks,
            "rollback_complete",
            "Status",
            "RolledBackByAdministratorId",
            "RollbackIdempotencyKey",
            "RollbackResultingRevision");
        AssertCheckClauseContains(
            checks,
            "rollback_revision",
            "RollbackResultingRevision",
            ">");
        AssertCheckClauseContains(
            checks,
            "rollback_time",
            "RolledBackAtUtc",
            "GrantedAtUtc",
            ">");
        AssertCheckClauseContains(checks, "status", "Status", "1", "2");
    }

    [Fact]
    public async Task GrantAndRollbackEvidenceRoundTripsAndHistorySurvivesOwnedItemRemoval()
    {
        var seed = await SeedProfileAsync();
        var grantId = Guid.NewGuid();
        var ownedItemId = OwnedItemId.From(Guid.NewGuid());
        var configurationVersionId = Guid.NewGuid();
        var administratorId = Guid.NewGuid();
        var grantKey = Guid.NewGuid();

        await using (var dbContext = _fixture.CreateDbContext())
        {
            await using var transaction = await new PlayerAssetsTransactionManager(dbContext)
                .BeginAsync();
            var profileRepository = new PlayerAssetsProfileRepository(dbContext);
            var grantRepository = new PlayerEquipmentGrantRepository(dbContext);
            var profile = Require(await profileRepository.FindByPlayerIdForUpdateAsync(seed.PlayerId));
            profile.GrantEquipment(profile.Revision, ownedItemId, "training-sword");
            await grantRepository.AddAsync(new PlayerEquipmentGrant(
                grantId,
                seed.PlayerId,
                ownedItemId,
                "training-sword",
                configurationVersionId,
                administratorId,
                GrantedAtUtc,
                "grant for persistence verification",
                grantKey,
                GrantFingerprint,
                profile.Revision,
                "127.0.0.1",
                "persistence-agent",
                "grant-correlation"));
            await transaction.CompleteAsync();
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var profile = Require(await new PlayerAssetsProfileRepository(dbContext)
                .FindByPlayerIdAsync(seed.PlayerId));
            var grant = Require(await new PlayerEquipmentGrantRepository(dbContext)
                .FindByGrantIdempotencyKeyAsync(seed.PlayerId, grantKey));

            Assert.Equal(2, profile.Revision);
            Assert.Equal(ownedItemId, Assert.Single(profile.OwnedItems).OwnedItemId);
            Assert.Equal(grantId, grant.GrantId);
            Assert.Equal(seed.PlayerId, grant.PlayerId);
            Assert.Equal(ownedItemId, grant.OwnedItemId);
            Assert.Equal("training-sword", grant.ConfiguredItemId);
            Assert.Equal(configurationVersionId, grant.ConfigurationVersionId);
            Assert.Equal(administratorId, grant.GrantedByAdministratorId);
            Assert.Equal(GrantedAtUtc, grant.GrantedAtUtc);
            Assert.Equal(DateTimeKind.Utc, grant.GrantedAtUtc.Kind);
            Assert.Equal("grant for persistence verification", grant.GrantReason);
            Assert.Equal(grantKey, grant.GrantIdempotencyKey);
            Assert.Equal(GrantFingerprint, grant.GrantRequestFingerprint);
            Assert.Equal(2, grant.GrantResultingRevision);
            Assert.Equal("127.0.0.1", grant.GrantIpAddress);
            Assert.Equal("persistence-agent", grant.GrantUserAgent);
            Assert.Equal("grant-correlation", grant.GrantCorrelationId);
            Assert.Equal(PlayerEquipmentGrantStatus.Active, grant.Status);
        }

        var rollbackAdministratorId = Guid.NewGuid();
        var rollbackKey = Guid.NewGuid();
        var rolledBackAtUtc = GrantedAtUtc.AddMinutes(5);
        await using (var dbContext = _fixture.CreateDbContext())
        {
            await using var transaction = await new PlayerAssetsTransactionManager(dbContext)
                .BeginAsync();
            var profileRepository = new PlayerAssetsProfileRepository(dbContext);
            var grantRepository = new PlayerEquipmentGrantRepository(dbContext);
            var profile = Require(await profileRepository.FindByPlayerIdForUpdateAsync(seed.PlayerId));
            var grant = Require(await grantRepository.FindByIdForUpdateAsync(seed.PlayerId, grantId));
            profile.RollbackGrantedEquipment(profile.Revision, grant.OwnedItemId);
            grant.Rollback(
                rollbackAdministratorId,
                rolledBackAtUtc,
                "rollback for persistence verification",
                rollbackKey,
                RollbackFingerprint,
                profile.Revision,
                "127.0.0.2",
                "rollback-agent",
                "rollback-correlation");
            await transaction.CompleteAsync();
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var profile = Require(await new PlayerAssetsProfileRepository(dbContext)
                .FindByPlayerIdAsync(seed.PlayerId));
            var grant = Require(await new PlayerEquipmentGrantRepository(dbContext)
                .FindByRollbackIdempotencyKeyAsync(seed.PlayerId, rollbackKey));

            Assert.Equal(3, profile.Revision);
            Assert.Empty(profile.OwnedItems);
            Assert.Equal(seed.PlayerId.Value, grant.PlayerId.Value);
            Assert.Equal(PlayerEquipmentGrantStatus.RolledBack, grant.Status);
            Assert.Equal(grantId, grant.GrantId);
            Assert.Equal(ownedItemId, grant.OwnedItemId);
            Assert.Equal("training-sword", grant.ConfiguredItemId);
            Assert.Equal(configurationVersionId, grant.ConfigurationVersionId);
            Assert.Equal(administratorId, grant.GrantedByAdministratorId);
            Assert.Equal(GrantedAtUtc, grant.GrantedAtUtc);
            Assert.Equal("grant for persistence verification", grant.GrantReason);
            Assert.Equal(grantKey, grant.GrantIdempotencyKey);
            Assert.Equal(GrantFingerprint, grant.GrantRequestFingerprint);
            Assert.Equal(2, grant.GrantResultingRevision);
            Assert.Equal("127.0.0.1", grant.GrantIpAddress);
            Assert.Equal("persistence-agent", grant.GrantUserAgent);
            Assert.Equal("grant-correlation", grant.GrantCorrelationId);
            Assert.Equal(rollbackAdministratorId, grant.RolledBackByAdministratorId);
            Assert.Equal(rolledBackAtUtc, grant.RolledBackAtUtc);
            Assert.Equal(DateTimeKind.Utc, grant.RolledBackAtUtc!.Value.Kind);
            Assert.Equal("rollback for persistence verification", grant.RollbackReason);
            Assert.Equal(rollbackKey, grant.RollbackIdempotencyKey);
            Assert.Equal(RollbackFingerprint, grant.RollbackRequestFingerprint);
            Assert.Equal(3, grant.RollbackResultingRevision);
            Assert.Equal("127.0.0.2", grant.RollbackIpAddress);
            Assert.Equal("rollback-agent", grant.RollbackUserAgent);
            Assert.Equal("rollback-correlation", grant.RollbackCorrelationId);
        }
    }

    [Fact]
    public async Task ListIsPlayerScopedNewestFirstPagedAndNoTracking()
    {
        var firstPlayer = await SeedProfileAsync();
        var secondPlayer = await SeedProfileAsync();
        var sameTime = GrantedAtUtc.AddHours(1);
        var firstTieId = Guid.NewGuid();
        var secondTieId = Guid.NewGuid();
        var grants = new[]
        {
            CreateGrant(firstPlayer.PlayerId, GrantedAtUtc, grantId: Guid.NewGuid()),
            CreateGrant(firstPlayer.PlayerId, sameTime, grantId: firstTieId),
            CreateGrant(firstPlayer.PlayerId, sameTime, grantId: secondTieId),
            CreateGrant(secondPlayer.PlayerId, sameTime.AddHours(1), grantId: Guid.NewGuid())
        };

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new PlayerEquipmentGrantRepository(dbContext);
            foreach (var grant in grants)
            {
                await repository.AddAsync(grant);
            }

            await dbContext.SaveChangesAsync();
        }

        var expectedTieOrder = new[] { firstTieId, secondTieId }
            .OrderByDescending(value => value.ToString("D"), StringComparer.Ordinal)
            .ToArray();
        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new PlayerEquipmentGrantRepository(dbContext);
            Assert.Equal(3, await repository.GetCountAsync(firstPlayer.PlayerId));
            var page = await repository.GetListAsync(
                firstPlayer.PlayerId,
                skipCount: 1,
                maxResultCount: 2);

            Assert.Equal([expectedTieOrder[1], grants[0].GrantId], page.Select(grant => grant.GrantId));
            Assert.Empty(dbContext.ChangeTracker.Entries<PlayerEquipmentGrant>());
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                repository.GetListAsync(firstPlayer.PlayerId, -1, 1));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                repository.GetListAsync(firstPlayer.PlayerId, 0, 0));
        }
    }

    [Fact]
    public async Task FindByIdForUpdateRequiresTransactionScopesByPlayerAndReturnsTrackedGrant()
    {
        var seed = await SeedProfileAsync();
        var grant = CreateGrant(seed.PlayerId, GrantedAtUtc);
        await PersistGrantAsync(grant);

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new PlayerEquipmentGrantRepository(dbContext);
            await Assert.ThrowsAsync<ArgumentException>(() =>
                repository.FindByIdForUpdateAsync(seed.PlayerId, Guid.Empty));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.FindByIdForUpdateAsync(seed.PlayerId, grant.GrantId));
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync();
            var repository = new PlayerEquipmentGrantRepository(dbContext);
            Assert.Null(await repository.FindByIdForUpdateAsync(
                PlayerId.From(Guid.NewGuid()),
                grant.GrantId));
            var tracked = Require(await repository.FindByIdForUpdateAsync(
                seed.PlayerId,
                grant.GrantId));
            Assert.Equal(seed.PlayerId.Value, tracked.PlayerId.Value);
            Assert.Equal(EntityState.Unchanged, dbContext.Entry(tracked).State);
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task ReceiptLookupsAreScopedByPlayerAndCommandKindAndNoTracking()
    {
        var firstPlayer = await SeedProfileAsync();
        var secondPlayer = await SeedProfileAsync();
        var sharedKey = Guid.NewGuid();
        var grantReceipt = CreateGrant(
            firstPlayer.PlayerId,
            GrantedAtUtc,
            grantKey: sharedKey);
        var rollbackReceipt = CreateGrant(
            firstPlayer.PlayerId,
            GrantedAtUtc.AddMinutes(1));
        rollbackReceipt.Rollback(
            Guid.NewGuid(),
            GrantedAtUtc.AddMinutes(2),
            "rollback receipt",
            sharedKey,
            RollbackFingerprint,
            3);
        var otherPlayerReceipt = CreateGrant(
            secondPlayer.PlayerId,
            GrantedAtUtc,
            grantKey: sharedKey);

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new PlayerEquipmentGrantRepository(dbContext);
            await repository.AddAsync(grantReceipt);
            await repository.AddAsync(rollbackReceipt);
            await repository.AddAsync(otherPlayerReceipt);
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new PlayerEquipmentGrantRepository(dbContext);
            Assert.Equal(
                grantReceipt.GrantId,
                Require(await repository.FindByGrantIdempotencyKeyAsync(
                    firstPlayer.PlayerId,
                    sharedKey)).GrantId);
            Assert.Equal(
                rollbackReceipt.GrantId,
                Require(await repository.FindByRollbackIdempotencyKeyAsync(
                    firstPlayer.PlayerId,
                    sharedKey)).GrantId);
            Assert.Equal(
                otherPlayerReceipt.GrantId,
                Require(await repository.FindByGrantIdempotencyKeyAsync(
                    secondPlayer.PlayerId,
                    sharedKey)).GrantId);
            Assert.Null(await repository.FindByRollbackIdempotencyKeyAsync(
                secondPlayer.PlayerId,
                sharedKey));
            Assert.Empty(dbContext.ChangeTracker.Entries<PlayerEquipmentGrant>());

            await Assert.ThrowsAsync<ArgumentException>(() =>
                repository.FindByGrantIdempotencyKeyAsync(
                    firstPlayer.PlayerId,
                    Guid.Empty));
            await Assert.ThrowsAsync<ArgumentException>(() =>
                repository.FindByRollbackIdempotencyKeyAsync(
                    firstPlayer.PlayerId,
                    Guid.Empty));
        }
    }

    [Fact]
    public async Task UniqueIndexesEnforcePerPlayerOwnedItemAndCommandKeys()
    {
        var firstPlayer = await SeedProfileAsync();
        var secondPlayer = await SeedProfileAsync();
        var sharedOwnedItemId = Guid.NewGuid();
        var sharedGrantKey = Guid.NewGuid();
        var sharedRollbackKey = Guid.NewGuid();
        var original = CreateGrant(
            firstPlayer.PlayerId,
            GrantedAtUtc,
            ownedItemId: sharedOwnedItemId,
            grantKey: sharedGrantKey);
        original.Rollback(
            Guid.NewGuid(),
            GrantedAtUtc.AddMinutes(1),
            "original rollback",
            sharedRollbackKey,
            RollbackFingerprint,
            3);
        await PersistGrantAsync(original);

        await AssertUniqueIndexViolationAsync(
            CreateGrant(
                firstPlayer.PlayerId,
                GrantedAtUtc.AddMinutes(2),
                ownedItemId: sharedOwnedItemId),
            "ux_sm_player_equipment_grants_player_item");
        await AssertUniqueIndexViolationAsync(
            CreateGrant(
                firstPlayer.PlayerId,
                GrantedAtUtc.AddMinutes(2),
                grantKey: sharedGrantKey),
            "ux_sm_player_equipment_grants_player_grant_key");
        var duplicateRollbackKey = CreateGrant(
            firstPlayer.PlayerId,
            GrantedAtUtc.AddMinutes(2));
        duplicateRollbackKey.Rollback(
            Guid.NewGuid(),
            GrantedAtUtc.AddMinutes(3),
            "duplicate rollback key",
            sharedRollbackKey,
            RollbackFingerprint,
            4);
        await AssertUniqueIndexViolationAsync(
            duplicateRollbackKey,
            "ux_sm_player_equipment_grants_player_rollback_key");

        var firstNullRollbackKey = CreateGrant(
            firstPlayer.PlayerId,
            GrantedAtUtc.AddMinutes(4));
        var secondNullRollbackKey = CreateGrant(
            firstPlayer.PlayerId,
            GrantedAtUtc.AddMinutes(5));
        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new PlayerEquipmentGrantRepository(dbContext);
            await repository.AddAsync(firstNullRollbackKey);
            await repository.AddAsync(secondNullRollbackKey);
            await dbContext.SaveChangesAsync();
        }

        var crossPlayerReuse = CreateGrant(
            secondPlayer.PlayerId,
            GrantedAtUtc,
            ownedItemId: sharedOwnedItemId,
            grantKey: sharedGrantKey);
        crossPlayerReuse.Rollback(
            Guid.NewGuid(),
            GrantedAtUtc.AddMinutes(1),
            "cross-player reuse",
            sharedRollbackKey,
            RollbackFingerprint,
            3);
        await PersistGrantAsync(crossPlayerReuse);

        await using var verificationContext = _fixture.CreateDbContext();
        var verificationRepository = new PlayerEquipmentGrantRepository(verificationContext);
        Assert.Equal(3, await verificationRepository.GetCountAsync(firstPlayer.PlayerId));
        Assert.Equal(1, await verificationRepository.GetCountAsync(secondPlayer.PlayerId));
    }

    [Fact]
    public async Task GrantForUpdateLockSerializesConcurrentTransactions()
    {
        var seed = await SeedProfileAsync();
        var grant = CreateGrant(seed.PlayerId, GrantedAtUtc);
        await PersistGrantAsync(grant);

        await using var firstContext = _fixture.CreateDbContext();
        await using var firstTransaction = await firstContext.Database.BeginTransactionAsync();
        var firstRepository = new PlayerEquipmentGrantRepository(firstContext);
        Assert.NotNull(await firstRepository.FindByIdForUpdateAsync(
            seed.PlayerId,
            grant.GrantId));

        await using var secondContext = _fixture.CreateDbContext();
        await using var secondTransaction = await secondContext.Database.BeginTransactionAsync();
        var secondRepository = new PlayerEquipmentGrantRepository(secondContext);
        var secondLock = secondRepository.FindByIdForUpdateAsync(
            seed.PlayerId,
            grant.GrantId);

        await Task.Delay(TimeSpan.FromMilliseconds(300));
        Assert.False(secondLock.IsCompleted);

        await firstTransaction.CommitAsync();
        Assert.NotNull(await secondLock.WaitAsync(TimeSpan.FromSeconds(5)));
        await secondTransaction.RollbackAsync();
    }

    [Fact]
    public async Task DisposingIncompleteTransactionRollsBackProfileAndGrantTogether()
    {
        var seed = await SeedProfileAsync();
        var grantId = Guid.NewGuid();

        await using (var dbContext = _fixture.CreateDbContext())
        {
            await using (var transaction = await new PlayerAssetsTransactionManager(dbContext)
                .BeginAsync())
            {
                var profileRepository = new PlayerAssetsProfileRepository(dbContext);
                var grantRepository = new PlayerEquipmentGrantRepository(dbContext);
                var lockedProfile = Require(await profileRepository.FindByPlayerIdForUpdateAsync(
                    seed.PlayerId));
                var ownedItemId = OwnedItemId.From(Guid.NewGuid());
                lockedProfile.GrantEquipment(
                    lockedProfile.Revision,
                    ownedItemId,
                    "training-sword");
                await grantRepository.AddAsync(CreateGrant(
                    seed.PlayerId,
                    GrantedAtUtc,
                    grantId,
                    ownedItemId.Value));
                await dbContext.SaveChangesAsync();
                Assert.Equal(1, await grantRepository.GetCountAsync(seed.PlayerId));
            }
        }

        await using var verificationContext = _fixture.CreateDbContext();
        var profile = Require(await new PlayerAssetsProfileRepository(verificationContext)
            .FindByPlayerIdAsync(seed.PlayerId));
        Assert.Equal(1, profile.Revision);
        Assert.Empty(profile.OwnedItems);
        Assert.Equal(
            0,
            await new PlayerEquipmentGrantRepository(verificationContext)
                .GetCountAsync(seed.PlayerId));
    }

    private async Task<ProfileSeed> SeedProfileAsync()
    {
        var seed = new ProfileSeed(
            PlayerId.From(Guid.NewGuid()),
            CharacterId.From(Guid.NewGuid()));
        var profile = new PlayerAssetsProfile(
            seed.PlayerId,
            new PlayerCharacter(
                seed.CharacterId,
                [AttributeValue.Create("attack", 10)]),
            []);
        await using var dbContext = _fixture.CreateDbContext();
        await new PlayerAssetsProfileRepository(dbContext).AddAsync(profile);
        await dbContext.SaveChangesAsync();
        return seed;
    }

    private async Task PersistGrantAsync(PlayerEquipmentGrant grant)
    {
        await using var dbContext = _fixture.CreateDbContext();
        await new PlayerEquipmentGrantRepository(dbContext).AddAsync(grant);
        await dbContext.SaveChangesAsync();
    }

    private async Task AssertUniqueIndexViolationAsync(
        PlayerEquipmentGrant grant,
        string expectedIndexName)
    {
        await using var dbContext = _fixture.CreateDbContext();
        await new PlayerEquipmentGrantRepository(dbContext).AddAsync(grant);
        var exception = await Assert.ThrowsAsync<DbUpdateException>(
            () => dbContext.SaveChangesAsync());
        var mySqlException = Assert.IsType<MySqlException>(exception.InnerException);
        Assert.Equal(1062, mySqlException.Number);
        Assert.Contains(expectedIndexName, mySqlException.Message, StringComparison.Ordinal);
    }

    private static PlayerEquipmentGrant CreateGrant(
        PlayerId playerId,
        DateTime grantedAtUtc,
        Guid? grantId = null,
        Guid? ownedItemId = null,
        Guid? grantKey = null)
    {
        return new PlayerEquipmentGrant(
            grantId ?? Guid.NewGuid(),
            playerId,
            OwnedItemId.From(ownedItemId ?? Guid.NewGuid()),
            "training-sword",
            Guid.NewGuid(),
            Guid.NewGuid(),
            grantedAtUtc,
            "persistence verification",
            grantKey ?? Guid.NewGuid(),
            GrantFingerprint,
            2);
    }

    private static IReadOnlyList<IndexColumn> ExpectedIndexes()
    {
        return
        [
            new("PRIMARY", true, 1, "GrantId"),
            new("ix_sm_player_equipment_grants_player_granted", false, 1, "PlayerId"),
            new("ix_sm_player_equipment_grants_player_granted", false, 2, "GrantedAtUtc"),
            new("ix_sm_player_equipment_grants_player_granted", false, 3, "GrantId"),
            new("ux_sm_player_equipment_grants_player_grant_key", true, 1, "PlayerId"),
            new("ux_sm_player_equipment_grants_player_grant_key", true, 2, "GrantIdempotencyKey"),
            new("ux_sm_player_equipment_grants_player_item", true, 1, "PlayerId"),
            new("ux_sm_player_equipment_grants_player_item", true, 2, "OwnedItemId"),
            new("ux_sm_player_equipment_grants_player_rollback_key", true, 1, "PlayerId"),
            new("ux_sm_player_equipment_grants_player_rollback_key", true, 2, "RollbackIdempotencyKey")
        ];
    }

    private static string IndexSortKey(IndexColumn value)
    {
        return $"{value.Name}:{value.Sequence:D2}";
    }

    private static void AssertCheckClauseContains(
        IEnumerable<CheckConstraint> checks,
        string nameSuffix,
        params string[] expectedFragments)
    {
        var check = Assert.Single(
            checks,
            candidate => candidate.Name.EndsWith(nameSuffix, StringComparison.Ordinal));
        foreach (var expectedFragment in expectedFragments)
        {
            Assert.Contains(expectedFragment, check.Clause, StringComparison.Ordinal);
        }
    }

    private static T Require<T>(T? value)
        where T : class
    {
        Assert.NotNull(value);
        return value;
    }

    private sealed record ColumnSchema(string Name, string Type, string Nullable);

    private sealed record IndexColumn(
        string Name,
        bool IsUnique,
        int Sequence,
        string Column);

    private sealed record CheckConstraint(string Name, string Clause, string Enforced);

    private sealed record ProfileSeed(PlayerId PlayerId, CharacterId CharacterId);
}
