using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NovaCore.BuildingBlocks.Infrastructure;
using NovaCore.BuildingBlocks.Permissions;
using SwordMaster.Application.Contracts.PlayerAssets;
using SwordMaster.Application.PlayerAssets;
using SwordMaster.Domain.PlayerAssets;
using SwordMaster.Host.EntityFrameworkCore;
using SwordMaster.Host.EntityFrameworkCore.PlayerAssets;
using Xunit;

namespace SwordMaster.Integration.Tests.PlayerAssets;

[Collection(SwordMasterMySqlCollection.Name)]
public sealed class PlayerEquipmentGrantMySqlApplicationTests
{
    private const string ConfiguredItemId = "training-sword";
    private const string GrantReason = "Customer support correction";
    private const string RollbackReason = "Correction approved by support lead";

    private static readonly Guid AdministratorId =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ConfigurationVersionId =
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTimeOffset GrantedAtUtc =
        new(2026, 8, 10, 3, 4, 5, TimeSpan.Zero);
    private static readonly DateTimeOffset RolledBackAtUtc =
        new(2026, 8, 10, 4, 5, 6, TimeSpan.Zero);
    private static readonly EquipmentSlot WeaponSlot = EquipmentSlot.Create("weapon");

    private readonly SwordMasterMySqlDatabaseFixture _fixture;

    public PlayerEquipmentGrantMySqlApplicationTests(
        SwordMasterMySqlDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GrantCommitReplayAndChangedFingerprintUseOnePersistedResult()
    {
        var seed = await SeedProfileAsync();
        var grantId = Guid.NewGuid();
        var ownedItemId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid();
        var definition = CreateDefinition();
        var grantContext = new PlayerEquipmentGrantMutationContext(
            ExpectedRevision: 1,
            idempotencyKey);
        var request = new GrantPlayerEquipmentRequest(
            ConfiguredItemId,
            $"  {GrantReason}  ");

        PlayerEquipmentGrantMutationResultDto firstResult;
        await using (var dbContext = _fixture.CreateDbContext())
        {
            firstResult = await CreateService(
                    dbContext,
                    new FixedDefinitionProvider(definition),
                    new SequenceGuidGenerator([grantId, ownedItemId]),
                    new FixedTimeProvider(GrantedAtUtc),
                    GrantAuditContext())
                .GrantAsync(seed.PlayerId.Value, request, grantContext);
        }

        Assert.Equal(grantId, firstResult.GrantId);
        Assert.Equal(ownedItemId, firstResult.OwnedItemId);
        Assert.Equal(2, firstResult.ResultingRevision);
        Assert.Equal(GrantReason, firstResult.GrantReason);
        Assert.Equal(GrantedAtUtc.UtcDateTime, firstResult.GrantedAtUtc);

        await AssertPersistedActiveGrantAsync(
            seed.PlayerId,
            idempotencyKey,
            grantId,
            ownedItemId);

        PlayerEquipmentGrantMutationResultDto replayResult;
        await using (var dbContext = _fixture.CreateDbContext())
        {
            replayResult = await CreateService(
                    dbContext,
                    new ThrowingDefinitionProvider(),
                    new ThrowingGuidGenerator(),
                    new FixedTimeProvider(RolledBackAtUtc),
                    new ThrowingAuditContextAccessor())
                .GrantAsync(seed.PlayerId.Value, request, grantContext);
        }

        Assert.Equal(firstResult, replayResult);

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var service = CreateService(
                dbContext,
                new ThrowingDefinitionProvider(),
                new ThrowingGuidGenerator(),
                new FixedTimeProvider(RolledBackAtUtc),
                new ThrowingAuditContextAccessor());
            var exception = await Assert.ThrowsAsync<PlayerEquipmentGrantException>(() =>
                service.GrantAsync(
                    seed.PlayerId.Value,
                    new GrantPlayerEquipmentRequest(
                        ConfiguredItemId,
                        "A different support reason"),
                    grantContext));

            Assert.Equal(
                PlayerEquipmentGrantErrorCodes.IdempotencyConflict,
                exception.Code);
        }

        await AssertPersistedActiveGrantAsync(
            seed.PlayerId,
            idempotencyKey,
            grantId,
            ownedItemId);
    }

    [Fact]
    public async Task EquippedRollbackRejectsThenUnequippedRollbackCommitsInProfileFirstLockOrder()
    {
        var seed = await SeedProfileAsync();
        var definition = CreateDefinition();
        var grantKey = Guid.NewGuid();
        var grantId = Guid.NewGuid();
        var ownedItemId = Guid.NewGuid();
        var grantResult = await GrantAsync(
            seed.PlayerId,
            definition,
            grantKey,
            grantId,
            ownedItemId);

        await EquipGrantedItemAsync(seed, definition, grantResult.OwnedItemId);

        var rollbackKey = Guid.NewGuid();
        var rollbackContext = new PlayerEquipmentGrantMutationContext(
            ExpectedRevision: 3,
            rollbackKey);
        var rollbackRequest = new RollbackPlayerEquipmentGrantRequest(
            $"  {RollbackReason}  ");

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var service = CreateService(
                dbContext,
                new ThrowingDefinitionProvider(),
                new ThrowingGuidGenerator(),
                new FixedTimeProvider(RolledBackAtUtc),
                RollbackAuditContext());
            var exception = await Assert.ThrowsAsync<PlayerEquipmentGrantException>(() =>
                service.RollbackAsync(
                    seed.PlayerId.Value,
                    grantId,
                    rollbackRequest,
                    rollbackContext));

            Assert.Equal(
                PlayerEquipmentGrantErrorCodes.RollbackBlockedItemEquipped,
                exception.Code);
        }

        await AssertEquippedActiveGrantAsync(seed, grantKey, grantId, ownedItemId);
        await UnequipGrantedItemAsync(seed);

        var commandRecorder = new LockingCommandRecorder();
        PlayerEquipmentGrantMutationResultDto rollbackResult;
        rollbackContext = new PlayerEquipmentGrantMutationContext(
            ExpectedRevision: 4,
            rollbackKey);
        await using (var dbContext = _fixture.CreateDbContext(commandRecorder))
        {
            rollbackResult = await CreateService(
                    dbContext,
                    new ThrowingDefinitionProvider(),
                    new ThrowingGuidGenerator(),
                    new FixedTimeProvider(RolledBackAtUtc),
                    RollbackAuditContext())
                .RollbackAsync(
                    seed.PlayerId.Value,
                    grantId,
                    rollbackRequest,
                    rollbackContext);
        }

        Assert.Equal(PlayerEquipmentGrantStatusDto.RolledBack, rollbackResult.Status);
        Assert.Equal(5, rollbackResult.ResultingRevision);
        Assert.Equal(RollbackReason, rollbackResult.RollbackReason);
        Assert.Equal(RolledBackAtUtc.UtcDateTime, rollbackResult.RolledBackAtUtc);
        AssertProfileBeforeGrantLockOrder(commandRecorder.LockingCommands);
        await AssertPersistedRollbackAsync(
            seed.PlayerId,
            grantKey,
            rollbackKey,
            grantId,
            ownedItemId);

        PlayerEquipmentGrantMutationResultDto replayResult;
        await using (var dbContext = _fixture.CreateDbContext())
        {
            replayResult = await CreateService(
                    dbContext,
                    new ThrowingDefinitionProvider(),
                    new ThrowingGuidGenerator(),
                    new FixedTimeProvider(GrantedAtUtc),
                    new ThrowingAuditContextAccessor())
                .RollbackAsync(
                    seed.PlayerId.Value,
                    grantId,
                    rollbackRequest,
                    rollbackContext);
        }

        Assert.Equal(rollbackResult, replayResult);

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var service = CreateService(
                dbContext,
                new ThrowingDefinitionProvider(),
                new ThrowingGuidGenerator(),
                new FixedTimeProvider(GrantedAtUtc),
                new ThrowingAuditContextAccessor());
            var exception = await Assert.ThrowsAsync<PlayerEquipmentGrantException>(() =>
                service.RollbackAsync(
                    seed.PlayerId.Value,
                    grantId,
                    new RollbackPlayerEquipmentGrantRequest(
                        "A different rollback reason"),
                    rollbackContext));

            Assert.Equal(
                PlayerEquipmentGrantErrorCodes.IdempotencyConflict,
                exception.Code);
        }

        await AssertPersistedRollbackAsync(
            seed.PlayerId,
            grantKey,
            rollbackKey,
            grantId,
            ownedItemId);
    }

    [Fact]
    public async Task ConcurrentSameKeyGrantsConvergeOnOnePersistedResult()
    {
        var seed = await SeedProfileAsync();
        var definition = CreateDefinition();
        var idempotencyKey = Guid.NewGuid();
        var request = new GrantPlayerEquipmentRequest(ConfiguredItemId, GrantReason);
        var context = new PlayerEquipmentGrantMutationContext(1, idempotencyKey);
        var firstLock = new ProfileLockContentionInterceptor(holdAfterAcquire: true);
        var secondLock = new ProfileLockContentionInterceptor(holdAfterAcquire: false);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        await using var firstDbContext = _fixture.CreateDbContext(firstLock);
        await using var secondDbContext = _fixture.CreateDbContext(secondLock);
        var firstService = CreateService(
            firstDbContext,
            new FixedDefinitionProvider(definition),
            new SequenceGuidGenerator([Guid.NewGuid(), Guid.NewGuid()]),
            new FixedTimeProvider(GrantedAtUtc),
            GrantAuditContext());
        var secondService = CreateService(
            secondDbContext,
            new FixedDefinitionProvider(definition),
            new SequenceGuidGenerator([Guid.NewGuid(), Guid.NewGuid()]),
            new FixedTimeProvider(GrantedAtUtc),
            GrantAuditContext());

        var firstTask = CaptureGrantAsync(
            firstService,
            seed.PlayerId,
            request,
            context,
            timeout.Token);
        Task<MutationOutcome>? secondTask = null;
        MutationOutcome[]? outcomes = null;
        try
        {
            await firstLock.WaitForProfileLockAcquiredAsync(timeout.Token);
            secondTask = CaptureGrantAsync(
                secondService,
                seed.PlayerId,
                request,
                context,
                timeout.Token);
            await secondLock.WaitForProfileLockAttemptedAsync(timeout.Token);
            await AssertContenderRemainsBlockedAsync(
                secondTask,
                secondLock,
                timeout.Token);
            firstLock.Release();
            await secondLock.WaitForProfileLockAcquiredAsync(timeout.Token);
            outcomes = await Task.WhenAll(firstTask, secondTask)
                .WaitAsync(timeout.Token);
        }
        finally
        {
            firstLock.Release();
            await DrainStartedGrantTasksAsync(
                timeout,
                firstTask,
                secondTask);
        }

        Assert.NotNull(outcomes);
        Assert.All(outcomes, outcome => Assert.Null(outcome.Exception));
        Assert.Equal(outcomes[0].Result, outcomes[1].Result);
        var result = Assert.IsType<PlayerEquipmentGrantMutationResultDto>(outcomes[0].Result);

        await using var verificationContext = _fixture.CreateDbContext();
        var profile = Require(await new PlayerAssetsProfileRepository(verificationContext)
            .FindByPlayerIdAsync(seed.PlayerId));
        var grantRepository = new PlayerEquipmentGrantRepository(verificationContext);
        var grant = Require(await grantRepository.FindByGrantIdempotencyKeyAsync(
            seed.PlayerId,
            idempotencyKey));
        Assert.Equal(2, profile.Revision);
        Assert.Equal(result.OwnedItemId, Assert.Single(profile.OwnedItems).OwnedItemId.Value);
        Assert.Equal(1, await grantRepository.GetCountAsync(seed.PlayerId));
        Assert.Equal(result.GrantId, grant.GrantId);
        Assert.Equal(result.OwnedItemId, grant.OwnedItemId.Value);
    }

    [Fact]
    public async Task ConcurrentDifferentKeyGrantsProduceOneSuccessAndOneStaleConflict()
    {
        var seed = await SeedProfileAsync();
        var definition = CreateDefinition();
        var request = new GrantPlayerEquipmentRequest(ConfiguredItemId, GrantReason);
        var firstKey = Guid.NewGuid();
        var secondKey = Guid.NewGuid();
        var firstLock = new ProfileLockContentionInterceptor(holdAfterAcquire: true);
        var secondLock = new ProfileLockContentionInterceptor(holdAfterAcquire: false);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        await using var firstDbContext = _fixture.CreateDbContext(firstLock);
        await using var secondDbContext = _fixture.CreateDbContext(secondLock);
        var firstService = CreateService(
            firstDbContext,
            new FixedDefinitionProvider(definition),
            new SequenceGuidGenerator([Guid.NewGuid(), Guid.NewGuid()]),
            new FixedTimeProvider(GrantedAtUtc),
            GrantAuditContext());
        var secondService = CreateService(
            secondDbContext,
            new FixedDefinitionProvider(definition),
            new SequenceGuidGenerator([Guid.NewGuid(), Guid.NewGuid()]),
            new FixedTimeProvider(GrantedAtUtc),
            GrantAuditContext());

        var firstTask = CaptureGrantAsync(
            firstService,
            seed.PlayerId,
            request,
            new PlayerEquipmentGrantMutationContext(1, firstKey),
            timeout.Token);
        Task<MutationOutcome>? secondTask = null;
        MutationOutcome[]? outcomes = null;
        try
        {
            await firstLock.WaitForProfileLockAcquiredAsync(timeout.Token);
            secondTask = CaptureGrantAsync(
                secondService,
                seed.PlayerId,
                request,
                new PlayerEquipmentGrantMutationContext(1, secondKey),
                timeout.Token);
            await secondLock.WaitForProfileLockAttemptedAsync(timeout.Token);
            await AssertContenderRemainsBlockedAsync(
                secondTask,
                secondLock,
                timeout.Token);
            firstLock.Release();
            await secondLock.WaitForProfileLockAcquiredAsync(timeout.Token);
            outcomes = await Task.WhenAll(firstTask, secondTask)
                .WaitAsync(timeout.Token);
        }
        finally
        {
            firstLock.Release();
            await DrainStartedGrantTasksAsync(
                timeout,
                firstTask,
                secondTask);
        }

        Assert.NotNull(outcomes);
        var success = Assert.IsType<PlayerEquipmentGrantMutationResultDto>(outcomes[0].Result);
        Assert.Null(outcomes[0].Exception);
        Assert.Null(outcomes[1].Result);
        var exception = Assert.IsType<PlayerEquipmentGrantException>(outcomes[1].Exception);
        Assert.Equal(PlayerEquipmentGrantErrorCodes.ConcurrencyConflict, exception.Code);

        await using var verificationContext = _fixture.CreateDbContext();
        var profile = Require(await new PlayerAssetsProfileRepository(verificationContext)
            .FindByPlayerIdAsync(seed.PlayerId));
        var grantRepository = new PlayerEquipmentGrantRepository(verificationContext);
        var grant = Assert.Single(await grantRepository.GetListAsync(seed.PlayerId, 0, 10));
        Assert.Equal(2, profile.Revision);
        Assert.Equal(success.OwnedItemId, Assert.Single(profile.OwnedItems).OwnedItemId.Value);
        Assert.Equal(success.GrantId, grant.GrantId);
        Assert.Equal(firstKey, grant.GrantIdempotencyKey);
    }

    [Fact]
    public async Task FailureAfterFlushBeforeCommitRollsBackProfileAndGrant()
    {
        var seed = await SeedProfileAsync();
        var definition = CreateDefinition();
        var idempotencyKey = Guid.NewGuid();
        var grantId = Guid.NewGuid();
        var ownedItemId = Guid.NewGuid();

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var transactionManager = new FlushThenFailTransactionManager(
                dbContext,
                new PlayerAssetsTransactionManager(dbContext));
            var service = CreateService(
                dbContext,
                new FixedDefinitionProvider(definition),
                new SequenceGuidGenerator([grantId, ownedItemId]),
                new FixedTimeProvider(GrantedAtUtc),
                GrantAuditContext(),
                transactionManager);

            await Assert.ThrowsAsync<ForcedTransactionFailureException>(() =>
                service.GrantAsync(
                    seed.PlayerId.Value,
                    new GrantPlayerEquipmentRequest(ConfiguredItemId, GrantReason),
                    new PlayerEquipmentGrantMutationContext(1, idempotencyKey)));
            Assert.True(transactionManager.FlushObserved);
        }

        await using var verificationContext = _fixture.CreateDbContext();
        var profile = Require(await new PlayerAssetsProfileRepository(verificationContext)
            .FindByPlayerIdAsync(seed.PlayerId));
        var grantRepository = new PlayerEquipmentGrantRepository(verificationContext);
        Assert.Equal(1, profile.Revision);
        Assert.Empty(profile.OwnedItems);
        Assert.Equal(0, await grantRepository.GetCountAsync(seed.PlayerId));
        Assert.Null(await grantRepository.FindByGrantIdempotencyKeyAsync(
            seed.PlayerId,
            idempotencyKey));
    }

    private async Task<PlayerEquipmentGrantMutationResultDto> GrantAsync(
        PlayerId playerId,
        EquipmentDefinition definition,
        Guid idempotencyKey,
        Guid grantId,
        Guid ownedItemId)
    {
        await using var dbContext = _fixture.CreateDbContext();
        return await CreateService(
                dbContext,
                new FixedDefinitionProvider(definition),
                new SequenceGuidGenerator([grantId, ownedItemId]),
                new FixedTimeProvider(GrantedAtUtc),
                GrantAuditContext())
            .GrantAsync(
                playerId.Value,
                new GrantPlayerEquipmentRequest(ConfiguredItemId, GrantReason),
                new PlayerEquipmentGrantMutationContext(1, idempotencyKey));
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

    private async Task EquipGrantedItemAsync(
        ProfileSeed seed,
        EquipmentDefinition definition,
        Guid ownedItemId)
    {
        await using var dbContext = _fixture.CreateDbContext();
        await using var transaction = await new PlayerAssetsTransactionManager(dbContext)
            .BeginAsync();
        var profile = Require(await new PlayerAssetsProfileRepository(dbContext)
            .FindByPlayerIdForUpdateAsync(seed.PlayerId));
        profile.Equip(
            profile.Revision,
            seed.CharacterId,
            WeaponSlot,
            OwnedItemId.From(ownedItemId),
            definition);
        await transaction.CompleteAsync();
    }

    private async Task UnequipGrantedItemAsync(ProfileSeed seed)
    {
        await using var dbContext = _fixture.CreateDbContext();
        await using var transaction = await new PlayerAssetsTransactionManager(dbContext)
            .BeginAsync();
        var profile = Require(await new PlayerAssetsProfileRepository(dbContext)
            .FindByPlayerIdForUpdateAsync(seed.PlayerId));
        profile.Unequip(profile.Revision, seed.CharacterId, WeaponSlot);
        await transaction.CompleteAsync();
    }

    private async Task AssertPersistedActiveGrantAsync(
        PlayerId playerId,
        Guid idempotencyKey,
        Guid expectedGrantId,
        Guid expectedOwnedItemId)
    {
        await using var dbContext = _fixture.CreateDbContext();
        var profile = Require(await new PlayerAssetsProfileRepository(dbContext)
            .FindByPlayerIdAsync(playerId));
        var repository = new PlayerEquipmentGrantRepository(dbContext);
        var grant = Require(await repository.FindByGrantIdempotencyKeyAsync(
            playerId,
            idempotencyKey));

        Assert.Equal(2, profile.Revision);
        Assert.Equal(expectedOwnedItemId, Assert.Single(profile.OwnedItems).OwnedItemId.Value);
        Assert.Equal(1, await repository.GetCountAsync(playerId));
        Assert.Equal(expectedGrantId, grant.GrantId);
        Assert.Equal(expectedOwnedItemId, grant.OwnedItemId.Value);
        Assert.Equal(ConfiguredItemId, grant.ConfiguredItemId);
        Assert.Equal(ConfigurationVersionId, grant.ConfigurationVersionId);
        Assert.Equal(PlayerEquipmentGrantStatus.Active, grant.Status);
        Assert.Equal(AdministratorId, grant.GrantedByAdministratorId);
        Assert.Equal(GrantedAtUtc.UtcDateTime, grant.GrantedAtUtc);
        Assert.Equal(DateTimeKind.Utc, grant.GrantedAtUtc.Kind);
        Assert.Equal(GrantReason, grant.GrantReason);
        Assert.Equal(idempotencyKey, grant.GrantIdempotencyKey);
        Assert.Equal(
            new PlayerEquipmentGrantRequestFingerprint().CreateGrant(
                playerId.Value,
                1,
                ConfiguredItemId,
                GrantReason),
            grant.GrantRequestFingerprint);
        Assert.Equal(2, grant.GrantResultingRevision);
        Assert.Equal("127.0.0.10", grant.GrantIpAddress);
        Assert.Equal("grant-integration-agent", grant.GrantUserAgent);
        Assert.Equal("grant-integration-correlation", grant.GrantCorrelationId);
        Assert.Null(grant.RollbackIdempotencyKey);
    }

    private async Task AssertEquippedActiveGrantAsync(
        ProfileSeed seed,
        Guid grantKey,
        Guid expectedGrantId,
        Guid expectedOwnedItemId)
    {
        await using var dbContext = _fixture.CreateDbContext();
        var profile = Require(await new PlayerAssetsProfileRepository(dbContext)
            .FindByPlayerIdAsync(seed.PlayerId));
        var grant = Require(await new PlayerEquipmentGrantRepository(dbContext)
            .FindByGrantIdempotencyKeyAsync(seed.PlayerId, grantKey));

        Assert.Equal(3, profile.Revision);
        Assert.Equal(expectedOwnedItemId, Assert.Single(profile.OwnedItems).OwnedItemId.Value);
        Assert.Equal(
            expectedOwnedItemId,
            Assert.Single(profile.EquipmentAssignments).OwnedItemId.Value);
        Assert.Equal(expectedGrantId, grant.GrantId);
        Assert.Equal(PlayerEquipmentGrantStatus.Active, grant.Status);
        Assert.Null(grant.RollbackIdempotencyKey);
        Assert.Null(grant.RolledBackAtUtc);
    }

    private async Task AssertPersistedRollbackAsync(
        PlayerId playerId,
        Guid grantKey,
        Guid rollbackKey,
        Guid expectedGrantId,
        Guid expectedOwnedItemId)
    {
        await using var dbContext = _fixture.CreateDbContext();
        var profile = Require(await new PlayerAssetsProfileRepository(dbContext)
            .FindByPlayerIdAsync(playerId));
        var repository = new PlayerEquipmentGrantRepository(dbContext);
        var grant = Require(await repository.FindByRollbackIdempotencyKeyAsync(
            playerId,
            rollbackKey));

        Assert.Equal(5, profile.Revision);
        Assert.Empty(profile.OwnedItems);
        Assert.Empty(profile.EquipmentAssignments);
        Assert.Equal(1, await repository.GetCountAsync(playerId));
        Assert.Equal(expectedGrantId, grant.GrantId);
        Assert.Equal(expectedOwnedItemId, grant.OwnedItemId.Value);
        Assert.Equal(ConfiguredItemId, grant.ConfiguredItemId);
        Assert.Equal(ConfigurationVersionId, grant.ConfigurationVersionId);
        Assert.Equal(PlayerEquipmentGrantStatus.RolledBack, grant.Status);
        Assert.Equal(AdministratorId, grant.GrantedByAdministratorId);
        Assert.Equal(GrantedAtUtc.UtcDateTime, grant.GrantedAtUtc);
        Assert.Equal(GrantReason, grant.GrantReason);
        Assert.Equal(grantKey, grant.GrantIdempotencyKey);
        Assert.Equal(2, grant.GrantResultingRevision);
        Assert.Equal("127.0.0.10", grant.GrantIpAddress);
        Assert.Equal("grant-integration-agent", grant.GrantUserAgent);
        Assert.Equal("grant-integration-correlation", grant.GrantCorrelationId);
        Assert.Equal(AdministratorId, grant.RolledBackByAdministratorId);
        Assert.Equal(RolledBackAtUtc.UtcDateTime, grant.RolledBackAtUtc);
        Assert.Equal(DateTimeKind.Utc, grant.RolledBackAtUtc!.Value.Kind);
        Assert.Equal(RollbackReason, grant.RollbackReason);
        Assert.Equal(rollbackKey, grant.RollbackIdempotencyKey);
        Assert.Equal(
            new PlayerEquipmentGrantRequestFingerprint().CreateRollback(
                playerId.Value,
                expectedGrantId,
                4,
                RollbackReason),
            grant.RollbackRequestFingerprint);
        Assert.Equal(5, grant.RollbackResultingRevision);
        Assert.Equal("127.0.0.20", grant.RollbackIpAddress);
        Assert.Equal("rollback-integration-agent", grant.RollbackUserAgent);
        Assert.Equal("rollback-integration-correlation", grant.RollbackCorrelationId);
    }

    private static PlayerEquipmentGrantAppService CreateService(
        SwordMasterDbContext dbContext,
        IEquipmentDefinitionProvider definitionProvider,
        IGuidGenerator guidGenerator,
        TimeProvider timeProvider,
        IPlayerEquipmentGrantAuditContextAccessor auditContextAccessor,
        IPlayerAssetsTransactionManager? transactionManager = null)
    {
        return new PlayerEquipmentGrantAppService(
            new FixedCurrentAdministrator(),
            new AllowEquipmentGrantPermissionChecker(),
            auditContextAccessor,
            definitionProvider,
            new PlayerAssetsProfileRepository(dbContext),
            new PlayerEquipmentGrantRepository(dbContext),
            transactionManager ?? new PlayerAssetsTransactionManager(dbContext),
            guidGenerator,
            timeProvider,
            new PlayerEquipmentGrantRequestFingerprint());
    }

    private static EquipmentDefinition CreateDefinition()
    {
        return new EquipmentDefinition(
            ConfiguredItemId,
            WeaponSlot,
            ConfigurationVersionId,
            [AttributeValue.Create("attack", 7)]);
    }

    private static IPlayerEquipmentGrantAuditContextAccessor GrantAuditContext()
    {
        return new FixedAuditContextAccessor(new PlayerEquipmentGrantAuditContext(
            "127.0.0.10",
            "grant-integration-agent",
            "grant-integration-correlation"));
    }

    private static IPlayerEquipmentGrantAuditContextAccessor RollbackAuditContext()
    {
        return new FixedAuditContextAccessor(new PlayerEquipmentGrantAuditContext(
            "127.0.0.20",
            "rollback-integration-agent",
            "rollback-integration-correlation"));
    }

    private static async Task<MutationOutcome> CaptureGrantAsync(
        PlayerEquipmentGrantAppService service,
        PlayerId playerId,
        GrantPlayerEquipmentRequest request,
        PlayerEquipmentGrantMutationContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            return new MutationOutcome(
                await service.GrantAsync(
                    playerId.Value,
                    request,
                    context,
                    cancellationToken),
                Exception: null);
        }
        catch (Exception exception)
        {
            return new MutationOutcome(Result: null, exception);
        }
    }

    private static async Task AssertContenderRemainsBlockedAsync(
        Task<MutationOutcome> contender,
        ProfileLockContentionInterceptor interceptor,
        CancellationToken cancellationToken)
    {
        var observationWindow = Task.Delay(
            TimeSpan.FromMilliseconds(300),
            cancellationToken);
        var completed = await Task.WhenAny(
            contender,
            interceptor.ProfileLockAcquired,
            observationWindow);

        Assert.Same(observationWindow, completed);
        await observationWindow;
        Assert.False(contender.IsCompleted);
        Assert.False(interceptor.ProfileLockAcquired.IsCompleted);
    }

    private static async Task DrainStartedGrantTasksAsync(
        CancellationTokenSource timeout,
        params Task<MutationOutcome>?[] tasks)
    {
        var startedTasks = tasks
            .Where(task => task is not null)
            .Cast<Task<MutationOutcome>>()
            .ToArray();
        if (startedTasks.Any(task => !task.IsCompleted))
        {
            timeout.Cancel();
        }

        await Task.WhenAll(startedTasks).WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static void AssertProfileBeforeGrantLockOrder(
        IReadOnlyList<string> lockingCommands)
    {
        var lastProfileLock = -1;
        var firstGrantLock = -1;
        for (var index = 0; index < lockingCommands.Count; index++)
        {
            var command = lockingCommands[index];
            if (command.Contains(
                    "sm_player_assets_profiles",
                    StringComparison.OrdinalIgnoreCase))
            {
                lastProfileLock = index;
            }

            if (firstGrantLock < 0 && command.Contains(
                    "sm_player_equipment_grants",
                    StringComparison.OrdinalIgnoreCase))
            {
                firstGrantLock = index;
            }
        }

        Assert.True(lastProfileLock >= 0, "The real application path did not lock the player profile.");
        Assert.True(firstGrantLock >= 0, "The real application path did not lock the grant row.");
        Assert.True(
            lastProfileLock < firstGrantLock,
            "Every profile FOR UPDATE command must precede the first grant FOR UPDATE command.");
    }

    private static T Require<T>(T? value)
        where T : class
    {
        Assert.NotNull(value);
        return value;
    }

    private sealed record ProfileSeed(PlayerId PlayerId, CharacterId CharacterId);

    private sealed record MutationOutcome(
        PlayerEquipmentGrantMutationResultDto? Result,
        Exception? Exception);

    private sealed class FixedCurrentAdministrator : ICurrentSwordMasterAdministrator
    {
        public bool IsAuthenticated => true;

        public Guid? AdministratorId => PlayerEquipmentGrantMySqlApplicationTests.AdministratorId;
    }

    private sealed class AllowEquipmentGrantPermissionChecker : IPermissionChecker
    {
        public Task<bool> IsGrantedAsync(
            string permissionName,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(string.Equals(
                permissionName,
                PlayerEquipmentGrantPermissions.Manage,
                StringComparison.Ordinal));
        }
    }

    private sealed class FixedDefinitionProvider(EquipmentDefinition definition) :
        IEquipmentDefinitionProvider
    {
        public Task<EquipmentDefinitionResolution> ResolveActiveAsync(
            string configuredItemId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(string.Equals(
                    configuredItemId,
                    definition.ConfiguredItemId,
                    StringComparison.Ordinal)
                ? EquipmentDefinitionResolution.Found(definition)
                : EquipmentDefinitionResolution.Missing());
        }
    }

    private sealed class ThrowingDefinitionProvider : IEquipmentDefinitionProvider
    {
        public Task<EquipmentDefinitionResolution> ResolveActiveAsync(
            string configuredItemId,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException(
                "A persisted replay must not resolve current configuration.");
        }
    }

    private sealed class SequenceGuidGenerator(IEnumerable<Guid> values) : IGuidGenerator
    {
        private readonly Queue<Guid> _values = new(values);

        public Guid Create()
        {
            return _values.Dequeue();
        }
    }

    private sealed class ThrowingGuidGenerator : IGuidGenerator
    {
        public Guid Create()
        {
            throw new InvalidOperationException(
                "A persisted replay must not generate identifiers.");
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }

    private sealed class FixedAuditContextAccessor(PlayerEquipmentGrantAuditContext context) :
        IPlayerEquipmentGrantAuditContextAccessor
    {
        public PlayerEquipmentGrantAuditContext Current => context;
    }

    private sealed class ThrowingAuditContextAccessor :
        IPlayerEquipmentGrantAuditContextAccessor
    {
        public PlayerEquipmentGrantAuditContext Current => throw new InvalidOperationException(
            "A persisted replay must not read current audit context.");
    }

    private sealed class ProfileLockContentionInterceptor(bool holdAfterAcquire) :
        DbCommandInterceptor
    {
        private static readonly TimeSpan CoordinationTimeout = TimeSpan.FromSeconds(10);

        private readonly TaskCompletionSource<bool> _profileLockAttempted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _profileLockAcquired =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _attemptRecorded;
        private int _acquisitionRecorded;

        public Task ProfileLockAcquired => _profileLockAcquired.Task;

        public async Task WaitForProfileLockAttemptedAsync(
            CancellationToken cancellationToken)
        {
            await _profileLockAttempted.Task.WaitAsync(
                CoordinationTimeout,
                cancellationToken);
        }

        public async Task WaitForProfileLockAcquiredAsync(
            CancellationToken cancellationToken)
        {
            await _profileLockAcquired.Task.WaitAsync(
                CoordinationTimeout,
                cancellationToken);
        }

        public void Release()
        {
            _release.TrySetResult(true);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (IsProfileLock(command.CommandText) &&
                Interlocked.CompareExchange(ref _attemptRecorded, 1, 0) == 0)
            {
                _profileLockAttempted.TrySetResult(true);
            }

            return base.ReaderExecutingAsync(
                command,
                eventData,
                result,
                cancellationToken);
        }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (IsProfileLock(command.CommandText) &&
                Interlocked.CompareExchange(ref _acquisitionRecorded, 1, 0) == 0)
            {
                _profileLockAcquired.TrySetResult(true);
                if (holdAfterAcquire)
                {
                    await _release.Task.WaitAsync(
                        CoordinationTimeout,
                        cancellationToken);
                }
            }

            return await base.ReaderExecutedAsync(
                command,
                eventData,
                result,
                cancellationToken);
        }

        private static bool IsProfileLock(string commandText)
        {
            return commandText.Contains(
                    "sm_player_assets_profiles",
                    StringComparison.OrdinalIgnoreCase) &&
                commandText.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class FlushThenFailTransactionManager(
        SwordMasterDbContext dbContext,
        IPlayerAssetsTransactionManager inner) : IPlayerAssetsTransactionManager
    {
        public bool FlushObserved { get; private set; }

        public async Task<IPlayerAssetsTransactionScope> BeginAsync(
            CancellationToken cancellationToken = default)
        {
            return new FlushThenFailTransactionScope(
                this,
                dbContext,
                await inner.BeginAsync(cancellationToken));
        }

        private sealed class FlushThenFailTransactionScope(
            FlushThenFailTransactionManager manager,
            SwordMasterDbContext dbContext,
            IPlayerAssetsTransactionScope inner) : IPlayerAssetsTransactionScope
        {
            public async Task CompleteAsync(
                CancellationToken cancellationToken = default)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                manager.FlushObserved = true;
                throw new ForcedTransactionFailureException();
            }

            public ValueTask DisposeAsync()
            {
                return inner.DisposeAsync();
            }
        }
    }

    private sealed class ForcedTransactionFailureException : Exception
    {
        public ForcedTransactionFailureException()
            : base("Forced failure after SaveChanges and before transaction commit.")
        {
        }
    }

    private sealed class LockingCommandRecorder : DbCommandInterceptor
    {
        private readonly object _sync = new();
        private readonly List<string> _lockingCommands = [];

        public IReadOnlyList<string> LockingCommands
        {
            get
            {
                lock (_sync)
                {
                    return _lockingCommands.ToArray();
                }
            }
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Record(command.CommandText);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Record(command.CommandText);
            return base.ReaderExecutingAsync(
                command,
                eventData,
                result,
                cancellationToken);
        }

        private void Record(string commandText)
        {
            if (!commandText.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            lock (_sync)
            {
                _lockingCommands.Add(commandText);
            }
        }
    }
}
