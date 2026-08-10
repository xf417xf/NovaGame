using Microsoft.EntityFrameworkCore;
using SwordMaster.Domain.PlayerAssets;
using SwordMaster.Host.EntityFrameworkCore;
using SwordMaster.Host.EntityFrameworkCore.PlayerAssets;
using Xunit;

namespace SwordMaster.Integration.Tests.PlayerAssets;

[Collection(SwordMasterMySqlCollection.Name)]
public sealed class PlayerAssetsPersistenceTests
{
    private readonly SwordMasterMySqlDatabaseFixture _fixture;

    public PlayerAssetsPersistenceTests(SwordMasterMySqlDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task UsesMySql84AndAppliesTheExplicitMigration()
    {
        await using var dbContext = _fixture.CreateDbContext();

        Assert.Equal("Pomelo.EntityFrameworkCore.MySql", dbContext.Database.ProviderName);
        Assert.True(await dbContext.Database.CanConnectAsync());
        Assert.Contains(
            SwordMasterMySqlDatabaseFixture.InitialMigrationId,
            await dbContext.Database.GetAppliedMigrationsAsync());
        Assert.False(dbContext.Database.HasPendingModelChanges());

        var connection = dbContext.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT VERSION(), @@session.time_zone, @@character_set_database";
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.StartsWith("8.4.", reader.GetString(0));
        Assert.Equal("+00:00", reader.GetString(1));
        Assert.Equal("utf8mb4", reader.GetString(2));
    }

    [Fact]
    public async Task MigrationCreatesOnlyTheExpectedPlayerAssetsTablesAndBinaryCodeCollations()
    {
        await using var dbContext = _fixture.CreateDbContext();
        var connection = dbContext.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var tableCommand = connection.CreateCommand();
        tableCommand.CommandText = """
            SELECT TABLE_NAME
            FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME LIKE 'sm\\_%'
            ORDER BY TABLE_NAME
            """;
        var actualTables = new List<string>();
        await using (var reader = await tableCommand.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                actualTables.Add(reader.GetString(0));
            }
        }

        Assert.Equal(
            [
                "sm_equipment_assignments",
                "sm_owned_equipment_items",
                "sm_player_asset_command_receipts",
                "sm_player_assets_profiles",
                "sm_player_character_base_attributes",
                "sm_player_characters"
            ],
            actualTables);

        await using var collationCommand = connection.CreateCommand();
        collationCommand.CommandText = """
            SELECT TABLE_NAME, COLUMN_NAME, COLLATION_NAME
            FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND (
                (TABLE_NAME = 'sm_equipment_assignments' AND COLUMN_NAME IN ('Slot', 'ConfiguredItemId'))
                OR (TABLE_NAME = 'sm_owned_equipment_items' AND COLUMN_NAME = 'ConfiguredItemId')
                OR (TABLE_NAME = 'sm_player_character_base_attributes' AND COLUMN_NAME = 'StatCode')
                OR (TABLE_NAME = 'sm_player_asset_command_receipts' AND COLUMN_NAME = 'RequestFingerprint')
              )
            """;
        await using var collationReader = await collationCommand.ExecuteReaderAsync();
        var checkedColumns = 0;
        while (await collationReader.ReadAsync())
        {
            checkedColumns++;
            Assert.Equal("utf8mb4_0900_bin", collationReader.GetString(2));
        }

        Assert.Equal(5, checkedColumns);
    }

    [Fact]
    public async Task AggregateAndPinnedModifierSnapshotRoundTripThroughRepositories()
    {
        var seed = await SeedProfileAsync();
        var configurationVersionId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid();

        await using (var dbContext = _fixture.CreateDbContext())
        {
            await using var transaction = await new PlayerAssetsTransactionManager(dbContext)
                .BeginAsync();
            var profileRepository = new PlayerAssetsProfileRepository(dbContext);
            var receiptRepository = new PlayerAssetCommandReceiptRepository(dbContext);
            var profile = Require(await profileRepository.FindByPlayerIdForUpdateAsync(
                PlayerId.From(seed.PlayerId)));

            profile.Equip(
                profile.Revision,
                CharacterId.From(seed.CharacterId),
                EquipmentSlot.Create("weapon"),
                OwnedItemId.From(seed.WeaponItemId),
                new EquipmentDefinition(
                    "training-sword",
                    EquipmentSlot.Create("weapon"),
                    configurationVersionId,
                    [
                        AttributeValue.Create("attack", 7),
                        AttributeValue.Create("health", 20)
                    ]));
            await receiptRepository.AddAsync(new PlayerAssetCommandReceipt(
                PlayerId.From(seed.PlayerId),
                idempotencyKey,
                "EQUIP-FINGERPRINT",
                profile.Revision,
                DateTime.UtcNow));

            await transaction.CompleteAsync();
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var profileRepository = new PlayerAssetsProfileRepository(dbContext);
            var receiptRepository = new PlayerAssetCommandReceiptRepository(dbContext);
            var profile = Require(await profileRepository.FindByPlayerIdAsync(
                PlayerId.From(seed.PlayerId)));
            var assignment = Assert.Single(profile.EquipmentAssignments);
            var receipt = Require(await receiptRepository.FindAsync(
                PlayerId.From(seed.PlayerId),
                idempotencyKey));

            Assert.Equal(2, profile.Revision);
            Assert.Equal(seed.CharacterId, profile.Character.CharacterId.Value);
            Assert.Equal(2, profile.Character.BaseAttributes.Count);
            Assert.Equal(2, profile.OwnedItems.Count);
            Assert.Equal(configurationVersionId, assignment.ConfigurationVersionId);
            Assert.Equal(seed.WeaponItemId, assignment.OwnedItemId.Value);
            Assert.Equal(
                ["attack:7", "health:20"],
                assignment.Modifiers.Select(modifier =>
                    $"{modifier.Code.Value}:{modifier.Value.Value}"));
            Assert.Equal(2, receipt.ResultingRevision);
            Assert.True(receipt.MatchesFingerprint("EQUIP-FINGERPRINT"));
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            await using var transaction = await new PlayerAssetsTransactionManager(dbContext)
                .BeginAsync();
            var repository = new PlayerAssetsProfileRepository(dbContext);
            var profile = Require(await repository.FindByPlayerIdForUpdateAsync(
                PlayerId.From(seed.PlayerId)));
            profile.Unequip(
                profile.Revision,
                CharacterId.From(seed.CharacterId),
                EquipmentSlot.Create("weapon"));
            await transaction.CompleteAsync();
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new PlayerAssetsProfileRepository(dbContext);
            var profile = Require(await repository.FindByPlayerIdAsync(
                PlayerId.From(seed.PlayerId)));
            Assert.Equal(3, profile.Revision);
            Assert.Empty(profile.EquipmentAssignments);
        }
    }

    [Fact]
    public async Task RootForUpdateLockSerializesCommandsForTheSamePlayer()
    {
        var seed = await SeedProfileAsync();
        await using var firstContext = _fixture.CreateDbContext();
        await using var firstTransaction = await firstContext.Database.BeginTransactionAsync();
        var firstRepository = new PlayerAssetsProfileRepository(firstContext);
        Assert.NotNull(await firstRepository.FindByPlayerIdForUpdateAsync(
            PlayerId.From(seed.PlayerId)));

        await using var secondContext = _fixture.CreateDbContext();
        await using var secondTransaction = await secondContext.Database.BeginTransactionAsync();
        var secondRepository = new PlayerAssetsProfileRepository(secondContext);
        var secondLock = secondRepository.FindByPlayerIdForUpdateAsync(
            PlayerId.From(seed.PlayerId));

        await Task.Delay(TimeSpan.FromMilliseconds(300));
        Assert.False(secondLock.IsCompleted);

        await firstTransaction.CommitAsync();
        Assert.NotNull(await secondLock.WaitAsync(TimeSpan.FromSeconds(5)));
        await secondTransaction.RollbackAsync();
    }

    [Fact]
    public async Task StaleRevisionSaveThrowsAndRollsBackTheLosingAssignment()
    {
        var seed = await SeedProfileAsync();
        await using var firstContext = _fixture.CreateDbContext();
        await using var secondContext = _fixture.CreateDbContext();
        var firstProfile = await LoadTrackedProfileAsync(firstContext, seed.PlayerId);
        var secondProfile = await LoadTrackedProfileAsync(secondContext, seed.PlayerId);

        firstProfile.Equip(
            firstProfile.Revision,
            CharacterId.From(seed.CharacterId),
            EquipmentSlot.Create("weapon"),
            OwnedItemId.From(seed.WeaponItemId),
            CreateDefinition("training-sword", "weapon", 7));
        secondProfile.Equip(
            secondProfile.Revision,
            CharacterId.From(seed.CharacterId),
            EquipmentSlot.Create("armor"),
            OwnedItemId.From(seed.ArmorItemId),
            CreateDefinition("training-armor", "armor", 3));

        await firstContext.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            secondContext.SaveChangesAsync());

        await using var verificationContext = _fixture.CreateDbContext();
        var repository = new PlayerAssetsProfileRepository(verificationContext);
        var persisted = Require(await repository.FindByPlayerIdAsync(
            PlayerId.From(seed.PlayerId)));
        var assignment = Assert.Single(persisted.EquipmentAssignments);
        Assert.Equal("weapon", assignment.Slot.Value);
        Assert.Equal(2, persisted.Revision);
    }

    [Fact]
    public async Task ReceiptKeyIsUniquePerPlayerButReusableByAnotherPlayer()
    {
        var idempotencyKey = Guid.NewGuid();
        var firstPlayerId = PlayerId.From(Guid.NewGuid());
        var secondPlayerId = PlayerId.From(Guid.NewGuid());

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new PlayerAssetCommandReceiptRepository(dbContext);
            await repository.AddAsync(CreateReceipt(firstPlayerId, idempotencyKey, "FIRST"));
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new PlayerAssetCommandReceiptRepository(dbContext);
            await repository.AddAsync(CreateReceipt(firstPlayerId, idempotencyKey, "DUPLICATE"));
            await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new PlayerAssetCommandReceiptRepository(dbContext);
            await repository.AddAsync(CreateReceipt(secondPlayerId, idempotencyKey, "SECOND"));
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            Assert.Equal(
                2,
                await dbContext.PlayerAssetCommandReceipts.CountAsync(receipt =>
                    receipt.IdempotencyKey == idempotencyKey));
        }
    }

    [Fact]
    public async Task DisposingIncompleteUnitOfWorkRollsBackProfileAndReceiptChanges()
    {
        var seed = await SeedProfileAsync();
        var idempotencyKey = Guid.NewGuid();

        await using (var dbContext = _fixture.CreateDbContext())
        {
            await using var transaction = await new PlayerAssetsTransactionManager(dbContext)
                .BeginAsync();
            var profileRepository = new PlayerAssetsProfileRepository(dbContext);
            var receiptRepository = new PlayerAssetCommandReceiptRepository(dbContext);
            var profile = Require(await profileRepository.FindByPlayerIdForUpdateAsync(
                PlayerId.From(seed.PlayerId)));
            profile.Equip(
                profile.Revision,
                CharacterId.From(seed.CharacterId),
                EquipmentSlot.Create("weapon"),
                OwnedItemId.From(seed.WeaponItemId),
                CreateDefinition("training-sword", "weapon", 9));
            await receiptRepository.AddAsync(CreateReceipt(
                PlayerId.From(seed.PlayerId),
                idempotencyKey,
                "ROLLBACK",
                profile.Revision));
            await dbContext.SaveChangesAsync();
        }

        await using var verificationContext = _fixture.CreateDbContext();
        var repository = new PlayerAssetsProfileRepository(verificationContext);
        var persisted = Require(await repository.FindByPlayerIdAsync(
            PlayerId.From(seed.PlayerId)));
        Assert.Equal(1, persisted.Revision);
        Assert.Empty(persisted.EquipmentAssignments);
        Assert.Null(await verificationContext.PlayerAssetCommandReceipts.SingleOrDefaultAsync(
            receipt => receipt.PlayerId == PlayerId.From(seed.PlayerId) &&
                receipt.IdempotencyKey == idempotencyKey));
    }

    [Fact]
    public async Task LockingRepositoriesRequireAnActiveTransaction()
    {
        await using var dbContext = _fixture.CreateDbContext();
        var profileRepository = new PlayerAssetsProfileRepository(dbContext);
        var receiptRepository = new PlayerAssetCommandReceiptRepository(dbContext);
        var playerId = PlayerId.From(Guid.NewGuid());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            profileRepository.FindByPlayerIdForUpdateAsync(playerId));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            receiptRepository.FindForUpdateAsync(playerId, Guid.NewGuid()));
    }

    private async Task<ProfileSeed> SeedProfileAsync()
    {
        var seed = new ProfileSeed(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid());
        var profile = new PlayerAssetsProfile(
            PlayerId.From(seed.PlayerId),
            new PlayerCharacter(
                CharacterId.From(seed.CharacterId),
                [
                    AttributeValue.Create("attack", 10),
                    AttributeValue.Create("health", 100)
                ]),
            [
                new OwnedEquipmentItem(
                    OwnedItemId.From(seed.WeaponItemId),
                    "training-sword"),
                new OwnedEquipmentItem(
                    OwnedItemId.From(seed.ArmorItemId),
                    "training-armor")
            ]);

        await using var dbContext = _fixture.CreateDbContext();
        var repository = new PlayerAssetsProfileRepository(dbContext);
        await repository.AddAsync(profile);
        await dbContext.SaveChangesAsync();
        return seed;
    }

    private static Task<PlayerAssetsProfile> LoadTrackedProfileAsync(
        SwordMasterDbContext dbContext,
        Guid playerId)
    {
        return dbContext.PlayerAssetsProfiles
            .Include(profile => profile.Character)
                .ThenInclude(character => character.BaseAttributes)
            .Include(profile => profile.OwnedItems)
            .Include(profile => profile.EquipmentAssignments)
            .AsSplitQuery()
            .SingleAsync(profile => profile.Id == playerId);
    }

    private static EquipmentDefinition CreateDefinition(
        string configuredItemId,
        string slot,
        int value)
    {
        return new EquipmentDefinition(
            configuredItemId,
            EquipmentSlot.Create(slot),
            Guid.NewGuid(),
            [AttributeValue.Create("attack", value)]);
    }

    private static PlayerAssetCommandReceipt CreateReceipt(
        PlayerId playerId,
        Guid idempotencyKey,
        string fingerprint,
        long revision = 1)
    {
        return new PlayerAssetCommandReceipt(
            playerId,
            idempotencyKey,
            fingerprint,
            revision,
            DateTime.UtcNow);
    }

    private static T Require<T>(T? value)
        where T : class
    {
        Assert.NotNull(value);
        return value;
    }

    private sealed record ProfileSeed(
        Guid PlayerId,
        Guid CharacterId,
        Guid WeaponItemId,
        Guid ArmorItemId);
}
