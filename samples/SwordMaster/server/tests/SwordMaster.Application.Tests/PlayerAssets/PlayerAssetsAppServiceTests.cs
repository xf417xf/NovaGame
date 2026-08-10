using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SwordMaster.Application.Contracts.PlayerAssets;
using SwordMaster.Application.PlayerAssets;
using SwordMaster.Domain.PlayerAssets;
using Xunit;
using ContractErrorCodes = SwordMaster.Application.Contracts.PlayerAssets.PlayerAssetsErrorCodes;

namespace SwordMaster.Application.Tests.PlayerAssets;

public sealed class PlayerAssetsAppServiceTests
{
    private static readonly Guid PlayerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CharacterId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid WeaponItemId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid SecondWeaponItemId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid ConfigurationVersionId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTimeOffset CompletedAt = new(
        2026,
        8,
        1,
        1,
        2,
        3,
        TimeSpan.Zero);

    [Fact]
    public async Task EquipCoordinatesConfigurationTransactionAndReceipt()
    {
        var fixture = CreateFixture();
        var request = new EquipEquipmentRequest(WeaponItemId);
        var mutationContext = new PlayerAssetsMutationContext(
            ExpectedRevision: 1,
            Guid.Parse("55555555-5555-5555-5555-555555555555"));

        var result = await fixture.Service.EquipAsync(
            CharacterId,
            " weapon ",
            request,
            mutationContext);

        Assert.Equal(2, result.Revision);
        Assert.Equal(2, fixture.Profile.Revision);
        var assignment = Assert.Single(fixture.Profile.EquipmentAssignments);
        Assert.Equal(ConfigurationVersionId, assignment.ConfigurationVersionId);
        Assert.Equal(1, fixture.DefinitionProvider.CallCount);
        Assert.Equal(1, fixture.TransactionManager.BeginCount);
        Assert.Equal(1, fixture.TransactionManager.CompleteCount);
        Assert.Equal(1, fixture.TransactionManager.DisposeCount);
        var receipt = Assert.Single(fixture.ReceiptRepository.Receipts);
        Assert.Equal(PlayerId, receipt.PlayerId.Value);
        Assert.Equal(result.Revision, receipt.ResultingRevision);
        Assert.Equal(CompletedAt.UtcDateTime, receipt.CompletedAtUtc);
    }

    [Fact]
    public async Task RepeatingSameRequestReturnsRecordedResultWithoutMutationDependencies()
    {
        var fixture = CreateFixture();
        var request = new EquipEquipmentRequest(WeaponItemId);
        var mutationContext = new PlayerAssetsMutationContext(
            ExpectedRevision: 1,
            Guid.Parse("66666666-6666-6666-6666-666666666666"));
        var first = await fixture.Service.EquipAsync(
            CharacterId,
            "weapon",
            request,
            mutationContext);
        var providerCallsAfterFirstRequest = fixture.DefinitionProvider.CallCount;
        var transactionBeginsAfterFirstRequest = fixture.TransactionManager.BeginCount;

        var replay = await fixture.Service.EquipAsync(
            CharacterId,
            "weapon",
            request,
            mutationContext);

        Assert.Equal(first, replay);
        Assert.Equal(providerCallsAfterFirstRequest, fixture.DefinitionProvider.CallCount);
        Assert.Equal(transactionBeginsAfterFirstRequest, fixture.TransactionManager.BeginCount);
        Assert.Single(fixture.Profile.EquipmentAssignments);
        Assert.Single(fixture.ReceiptRepository.Receipts);
    }

    [Fact]
    public async Task ReusingKeyForDifferentRequestReturnsIdempotencyConflict()
    {
        var fixture = CreateFixture();
        var idempotencyKey = Guid.Parse("77777777-7777-7777-7777-777777777777");
        await fixture.Service.EquipAsync(
            CharacterId,
            "weapon",
            new EquipEquipmentRequest(WeaponItemId),
            new PlayerAssetsMutationContext(1, idempotencyKey));

        var exception = await Assert.ThrowsAsync<PlayerAssetsException>(() =>
            fixture.Service.EquipAsync(
                CharacterId,
                "weapon",
                new EquipEquipmentRequest(SecondWeaponItemId),
                new PlayerAssetsMutationContext(1, idempotencyKey)));

        Assert.Equal(ContractErrorCodes.IdempotencyConflict, exception.Code);
        Assert.Equal(PlayerAssetsFailureKind.Conflict, exception.FailureKind);
        Assert.Equal(1, fixture.DefinitionProvider.CallCount);
        Assert.Single(fixture.Profile.EquipmentAssignments);
    }

    [Fact]
    public async Task ReceiptIsRecheckedInsideTransactionBeforeMutation()
    {
        var fixture = CreateFixture();
        var idempotencyKey = Guid.Parse("88888888-8888-8888-8888-888888888888");
        var request = new EquipEquipmentRequest(WeaponItemId);
        var mutationContext = new PlayerAssetsMutationContext(1, idempotencyKey);
        fixture.ReceiptRepository.OnFindForUpdate = (playerId, key) =>
        {
            Assert.Equal(1, fixture.ProfileRepository.WriteReadCount);
            return new PlayerAssetCommandReceipt(
                playerId,
                key,
                CreateFingerprint(
                    "equip",
                    CharacterId,
                    "weapon",
                    expectedRevision: 1,
                    WeaponItemId),
                resultingRevision: 9,
                CompletedAt.UtcDateTime);
        };

        var result = await fixture.Service.EquipAsync(
            CharacterId,
            "weapon",
            request,
            mutationContext);

        Assert.Equal(9, result.Revision);
        Assert.Equal(1, fixture.Profile.Revision);
        Assert.Empty(fixture.Profile.EquipmentAssignments);
        Assert.Equal(1, fixture.DefinitionProvider.CallCount);
        Assert.Equal(1, fixture.TransactionManager.CompleteCount);
        Assert.Empty(fixture.ReceiptRepository.Receipts);
    }

    [Fact]
    public async Task StaleRevisionMapsDomainFailureAndDoesNotCommit()
    {
        var fixture = CreateFixture();

        var exception = await Assert.ThrowsAsync<PlayerAssetsException>(() =>
            fixture.Service.EquipAsync(
                CharacterId,
                "weapon",
                new EquipEquipmentRequest(WeaponItemId),
                new PlayerAssetsMutationContext(
                    ExpectedRevision: 99,
                    Guid.Parse("99999999-9999-9999-9999-999999999999"))));

        Assert.Equal(ContractErrorCodes.ConcurrencyConflict, exception.Code);
        Assert.Equal(PlayerAssetsFailureKind.Conflict, exception.FailureKind);
        Assert.Equal(1, fixture.Profile.Revision);
        Assert.Empty(fixture.Profile.EquipmentAssignments);
        Assert.Equal(1, fixture.TransactionManager.BeginCount);
        Assert.Equal(0, fixture.TransactionManager.CompleteCount);
        Assert.Equal(1, fixture.TransactionManager.DisposeCount);
        Assert.Empty(fixture.ReceiptRepository.Receipts);
    }

    [Fact]
    public async Task UnequipRemovesAssignmentAndRecordsNewRevision()
    {
        var fixture = CreateFixture();
        fixture.Profile.Equip(
            fixture.Profile.Revision,
            Domain.PlayerAssets.CharacterId.From(CharacterId),
            EquipmentSlot.Create("weapon"),
            OwnedItemId.From(WeaponItemId),
            CreateDefinition());

        var result = await fixture.Service.UnequipAsync(
            CharacterId,
            "weapon",
            new PlayerAssetsMutationContext(
                ExpectedRevision: 2,
                Guid.Parse("12121212-1212-1212-1212-121212121212")));

        Assert.Equal(3, result.Revision);
        Assert.Empty(fixture.Profile.EquipmentAssignments);
        Assert.Single(fixture.ReceiptRepository.Receipts);
        Assert.Equal(1, fixture.TransactionManager.CompleteCount);
    }

    [Fact]
    public async Task GetAttributesMapsDeterministicBreakdownAndPinnedAssignment()
    {
        var fixture = CreateFixture();
        fixture.Profile.Equip(
            fixture.Profile.Revision,
            Domain.PlayerAssets.CharacterId.From(CharacterId),
            EquipmentSlot.Create("weapon"),
            OwnedItemId.From(WeaponItemId),
            CreateDefinition());

        var result = await fixture.Service.GetAttributesAsync(CharacterId);

        Assert.Equal(2, result.Revision);
        var attack = Assert.Single(result.Attributes, attribute => attribute.StatCode == "attack");
        Assert.Equal(10, attack.BaseValue);
        Assert.Equal(7, attack.EquipmentValue);
        Assert.Equal(17, attack.EffectiveValue);
        var health = Assert.Single(result.Attributes, attribute => attribute.StatCode == "health");
        Assert.Equal(100, health.BaseValue);
        Assert.Equal(0, health.EquipmentValue);
        Assert.Equal(100, health.EffectiveValue);
        var assignment = Assert.Single(result.EquipmentAssignments);
        Assert.Equal("weapon", assignment.Slot);
        Assert.Equal(WeaponItemId, assignment.OwnedItemId);
        Assert.Equal(ConfigurationVersionId, assignment.ConfigurationVersionId);
        Assert.Equal(7, Assert.Single(assignment.Modifiers).Value);
    }

    [Fact]
    public async Task UnauthenticatedPlayerIsRejectedBeforeRepositoryAccess()
    {
        var fixture = CreateFixture(isAuthenticated: false);

        var exception = await Assert.ThrowsAsync<PlayerAssetsException>(() =>
            fixture.Service.GetAttributesAsync(CharacterId));

        Assert.Equal(ContractErrorCodes.UnauthorizedPlayer, exception.Code);
        Assert.Equal(PlayerAssetsFailureKind.Unauthorized, exception.FailureKind);
        Assert.Equal(0, fixture.ProfileRepository.ReadCount);
    }

    [Fact]
    public async Task MissingActiveDefinitionFailsBeforeTransaction()
    {
        var fixture = CreateFixture(
            definitionResolution: EquipmentDefinitionResolution.Missing());

        var exception = await Assert.ThrowsAsync<PlayerAssetsException>(() =>
            fixture.Service.EquipAsync(
                CharacterId,
                "weapon",
                new EquipEquipmentRequest(WeaponItemId),
                new PlayerAssetsMutationContext(
                    ExpectedRevision: 1,
                    Guid.Parse("13131313-1313-1313-1313-131313131313"))));

        Assert.Equal(ContractErrorCodes.ItemNotEquipable, exception.Code);
        Assert.Equal(PlayerAssetsFailureKind.UnprocessableEntity, exception.FailureKind);
        Assert.Equal(0, fixture.TransactionManager.BeginCount);
        Assert.Empty(fixture.ReceiptRepository.Receipts);
    }

    [Fact]
    public async Task InvalidActiveDefinitionMapsToInvalidModifierBeforeTransaction()
    {
        var fixture = CreateFixture(
            definitionResolution: EquipmentDefinitionResolution.Invalid());

        var exception = await Assert.ThrowsAsync<PlayerAssetsException>(() =>
            fixture.Service.EquipAsync(
                CharacterId,
                "weapon",
                new EquipEquipmentRequest(WeaponItemId),
                new PlayerAssetsMutationContext(
                    ExpectedRevision: 1,
                    Guid.Parse("14141414-1414-1414-1414-141414141414"))));

        Assert.Equal(ContractErrorCodes.InvalidModifier, exception.Code);
        Assert.Equal(PlayerAssetsFailureKind.UnprocessableEntity, exception.FailureKind);
        Assert.Equal(0, fixture.TransactionManager.BeginCount);
        Assert.Empty(fixture.ReceiptRepository.Receipts);
    }

    [Theory]
    [InlineData("character-not-found", ContractErrorCodes.CharacterNotFound, PlayerAssetsFailureKind.NotFound)]
    [InlineData("owned-item-not-found", ContractErrorCodes.OwnedItemNotFound, PlayerAssetsFailureKind.NotFound)]
    [InlineData("item-not-equipable", ContractErrorCodes.ItemNotEquipable, PlayerAssetsFailureKind.UnprocessableEntity)]
    [InlineData("slot-mismatch", ContractErrorCodes.SlotMismatch, PlayerAssetsFailureKind.UnprocessableEntity)]
    [InlineData("item-already-equipped", ContractErrorCodes.ItemAlreadyEquipped, PlayerAssetsFailureKind.Conflict)]
    [InlineData("slot-occupied", ContractErrorCodes.SlotOccupied, PlayerAssetsFailureKind.Conflict)]
    [InlineData("slot-not-equipped", ContractErrorCodes.SlotNotEquipped, PlayerAssetsFailureKind.NotFound)]
    [InlineData("modifier-overflow", ContractErrorCodes.InvalidModifier, PlayerAssetsFailureKind.UnprocessableEntity)]
    public async Task DomainFailuresMapToStableApplicationErrors(
        string scenario,
        string expectedCode,
        PlayerAssetsFailureKind expectedFailureKind)
    {
        var exception = await InvokeDomainFailureAsync(scenario);

        Assert.Equal(expectedCode, exception.Code);
        Assert.Equal(expectedFailureKind, exception.FailureKind);
    }

    [Theory]
    [InlineData("empty-character")]
    [InlineData("empty-owned-item")]
    [InlineData("invalid-slot")]
    [InlineData("dot-slot")]
    [InlineData("path-separator-slot")]
    [InlineData("nonpositive-revision")]
    [InlineData("empty-idempotency-key")]
    public async Task InvalidMutationInputsReturnStableBadRequest(string scenario)
    {
        var fixture = CreateFixture();

        var exception = await Assert.ThrowsAsync<PlayerAssetsException>(() =>
            InvokeInvalidMutationAsync(fixture, scenario));

        Assert.Equal(ContractErrorCodes.InvalidRequest, exception.Code);
        Assert.Equal(PlayerAssetsFailureKind.BadRequest, exception.FailureKind);
        Assert.Equal(0, fixture.TransactionManager.BeginCount);
        Assert.Empty(fixture.ReceiptRepository.Receipts);
    }

    private static async Task<PlayerAssetsException> InvokeDomainFailureAsync(string scenario)
    {
        var profile = CreateProfile(scenario == "modifier-overflow" ? int.MaxValue : 10);
        EquipmentDefinitionResolution? definitionResolution = scenario switch
        {
            "item-not-equipable" => EquipmentDefinitionResolution.Found(
                CreateDefinition(configuredItemId: "different-item")),
            "slot-mismatch" => EquipmentDefinitionResolution.Found(
                CreateDefinition(slot: "armor")),
            _ => null
        };

        if (scenario is "item-already-equipped" or "slot-occupied")
        {
            profile.Equip(
                profile.Revision,
                Domain.PlayerAssets.CharacterId.From(CharacterId),
                EquipmentSlot.Create("weapon"),
                OwnedItemId.From(WeaponItemId),
                CreateDefinition());
            definitionResolution = scenario == "item-already-equipped"
                ? EquipmentDefinitionResolution.Found(CreateDefinition(slot: "off-hand"))
                : EquipmentDefinitionResolution.Found(
                    CreateDefinition(configuredItemId: "second-sword"));
        }

        var fixture = CreateFixture(
            definitionResolution: definitionResolution,
            profile: profile);
        var idempotencyKey = Guid.Parse("15151515-1515-1515-1515-151515151515");
        Func<Task> action = scenario switch
        {
            "character-not-found" => () => fixture.Service.GetAttributesAsync(
                Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff")),
            "owned-item-not-found" => () => fixture.Service.EquipAsync(
                CharacterId,
                "weapon",
                new EquipEquipmentRequest(
                    Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee")),
                new PlayerAssetsMutationContext(1, idempotencyKey)),
            "item-not-equipable" or "slot-mismatch" or "modifier-overflow" => () =>
                fixture.Service.EquipAsync(
                    CharacterId,
                    "weapon",
                    new EquipEquipmentRequest(WeaponItemId),
                    new PlayerAssetsMutationContext(1, idempotencyKey)),
            "item-already-equipped" => () => fixture.Service.EquipAsync(
                CharacterId,
                "off-hand",
                new EquipEquipmentRequest(WeaponItemId),
                new PlayerAssetsMutationContext(2, idempotencyKey)),
            "slot-occupied" => () => fixture.Service.EquipAsync(
                CharacterId,
                "weapon",
                new EquipEquipmentRequest(SecondWeaponItemId),
                new PlayerAssetsMutationContext(2, idempotencyKey)),
            "slot-not-equipped" => () => fixture.Service.UnequipAsync(
                CharacterId,
                "weapon",
                new PlayerAssetsMutationContext(1, idempotencyKey)),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null)
        };

        return await Assert.ThrowsAsync<PlayerAssetsException>(action);
    }

    private static Task InvokeInvalidMutationAsync(TestFixture fixture, string scenario)
    {
        var characterId = scenario == "empty-character" ? Guid.Empty : CharacterId;
        var ownedItemId = scenario == "empty-owned-item" ? Guid.Empty : WeaponItemId;
        var slot = scenario switch
        {
            "invalid-slot" => "two words",
            "dot-slot" => "..",
            "path-separator-slot" => "main/hand",
            _ => "weapon"
        };
        var expectedRevision = scenario == "nonpositive-revision" ? 0 : 1;
        var idempotencyKey = scenario == "empty-idempotency-key"
            ? Guid.Empty
            : Guid.Parse("16161616-1616-1616-1616-161616161616");
        return fixture.Service.EquipAsync(
            characterId,
            slot,
            new EquipEquipmentRequest(ownedItemId),
            new PlayerAssetsMutationContext(expectedRevision, idempotencyKey));
    }

    private static TestFixture CreateFixture(
        bool isAuthenticated = true,
        EquipmentDefinitionResolution? definitionResolution = null,
        PlayerAssetsProfile? profile = null)
    {
        var actualProfile = profile ?? CreateProfile();
        var profileRepository = new InMemoryProfileRepository(actualProfile);
        var receiptRepository = new InMemoryReceiptRepository();
        var definitionProvider = new RecordingDefinitionProvider(
            definitionResolution ?? EquipmentDefinitionResolution.Found(CreateDefinition()));
        var transactionManager = new RecordingTransactionManager();
        var service = new PlayerAssetsAppService(
            new TestCurrentPlayerContext(isAuthenticated, isAuthenticated ? PlayerId : null),
            definitionProvider,
            profileRepository,
            receiptRepository,
            transactionManager,
            new FixedTimeProvider(CompletedAt));
        return new TestFixture(
            service,
            actualProfile,
            profileRepository,
            receiptRepository,
            definitionProvider,
            transactionManager);
    }

    private static PlayerAssetsProfile CreateProfile(int baseAttack = 10)
    {
        return new PlayerAssetsProfile(
            Domain.PlayerAssets.PlayerId.From(PlayerId),
            new PlayerCharacter(
                Domain.PlayerAssets.CharacterId.From(CharacterId),
                [
                    AttributeValue.Create("attack", baseAttack),
                    AttributeValue.Create("health", 100)
                ]),
            [
                new OwnedEquipmentItem(OwnedItemId.From(WeaponItemId), "training-sword"),
                new OwnedEquipmentItem(OwnedItemId.From(SecondWeaponItemId), "second-sword")
            ]);
    }

    private static EquipmentDefinition CreateDefinition(
        string configuredItemId = "training-sword",
        string slot = "weapon")
    {
        return new EquipmentDefinition(
            configuredItemId,
            EquipmentSlot.Create(slot),
            ConfigurationVersionId,
            [AttributeValue.Create("attack", 7)]);
    }

    private static string CreateFingerprint(
        string command,
        Guid characterId,
        string slot,
        long expectedRevision,
        Guid? ownedItemId = null)
    {
        var canonical = string.Join(
            '|',
            command,
            characterId.ToString("D"),
            slot,
            expectedRevision.ToString(CultureInfo.InvariantCulture),
            ownedItemId?.ToString("D") ?? string.Empty);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private sealed record TestFixture(
        PlayerAssetsAppService Service,
        PlayerAssetsProfile Profile,
        InMemoryProfileRepository ProfileRepository,
        InMemoryReceiptRepository ReceiptRepository,
        RecordingDefinitionProvider DefinitionProvider,
        RecordingTransactionManager TransactionManager);

    private sealed record TestCurrentPlayerContext(
        bool IsAuthenticated,
        Guid? PlayerId) : ICurrentPlayerContext;

    private sealed class RecordingDefinitionProvider(
        EquipmentDefinitionResolution resolution) : IEquipmentDefinitionProvider
    {
        public int CallCount { get; private set; }

        public Task<EquipmentDefinitionResolution> ResolveActiveAsync(
            string configuredItemId,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(resolution);
        }
    }

    private sealed class InMemoryProfileRepository(
        PlayerAssetsProfile? profile) : IPlayerAssetsProfileRepository
    {
        public int ReadCount { get; private set; }

        public int WriteReadCount { get; private set; }

        public Task<PlayerAssetsProfile?> FindByPlayerIdAsync(
            Domain.PlayerAssets.PlayerId playerId,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return Task.FromResult(ProfileBelongsTo(playerId) ? profile : null);
        }

        public Task<PlayerAssetsProfile?> FindByPlayerIdForUpdateAsync(
            Domain.PlayerAssets.PlayerId playerId,
            CancellationToken cancellationToken = default)
        {
            WriteReadCount++;
            return Task.FromResult(ProfileBelongsTo(playerId) ? profile : null);
        }

        public Task AddAsync(
            PlayerAssetsProfile newProfile,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Application tests do not create player profiles.");
        }

        private bool ProfileBelongsTo(Domain.PlayerAssets.PlayerId playerId)
        {
            return profile is not null && profile.PlayerId.Equals(playerId);
        }
    }

    private sealed class InMemoryReceiptRepository : IPlayerAssetCommandReceiptRepository
    {
        private readonly List<PlayerAssetCommandReceipt> _receipts = [];

        public IReadOnlyCollection<PlayerAssetCommandReceipt> Receipts => _receipts.AsReadOnly();

        public Func<Domain.PlayerAssets.PlayerId, Guid, PlayerAssetCommandReceipt?>? OnFindForUpdate { get; set; }

        public Task<PlayerAssetCommandReceipt?> FindAsync(
            Domain.PlayerAssets.PlayerId playerId,
            Guid idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            var receipt = Find(playerId, idempotencyKey);
            return Task.FromResult(receipt);
        }

        public Task<PlayerAssetCommandReceipt?> FindForUpdateAsync(
            Domain.PlayerAssets.PlayerId playerId,
            Guid idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            var receipt = OnFindForUpdate?.Invoke(playerId, idempotencyKey) ??
                Find(playerId, idempotencyKey);
            return Task.FromResult(receipt);
        }

        public Task AddAsync(
            PlayerAssetCommandReceipt receipt,
            CancellationToken cancellationToken = default)
        {
            _receipts.Add(receipt);
            return Task.CompletedTask;
        }

        private PlayerAssetCommandReceipt? Find(
            Domain.PlayerAssets.PlayerId playerId,
            Guid idempotencyKey)
        {
            return _receipts.SingleOrDefault(receipt =>
                receipt.PlayerId.Equals(playerId) &&
                receipt.IdempotencyKey == idempotencyKey);
        }
    }

    private sealed class RecordingTransactionManager : IPlayerAssetsTransactionManager
    {
        public int BeginCount { get; private set; }

        public int CompleteCount { get; private set; }

        public int DisposeCount { get; private set; }

        public Task<IPlayerAssetsTransactionScope> BeginAsync(
            CancellationToken cancellationToken = default)
        {
            BeginCount++;
            return Task.FromResult<IPlayerAssetsTransactionScope>(
                new RecordingTransactionScope(this));
        }

        private sealed class RecordingTransactionScope(
            RecordingTransactionManager manager) : IPlayerAssetsTransactionScope
        {
            public Task CompleteAsync(CancellationToken cancellationToken = default)
            {
                manager.CompleteCount++;
                return Task.CompletedTask;
            }

            public ValueTask DisposeAsync()
            {
                manager.DisposeCount++;
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
