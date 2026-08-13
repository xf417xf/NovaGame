using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using NovaCore.BuildingBlocks.Infrastructure;
using NovaCore.BuildingBlocks.Permissions;
using SwordMaster.Application.Contracts;
using SwordMaster.Application.Contracts.PlayerAssets;
using SwordMaster.Application.PlayerAssets;
using SwordMaster.Domain.PlayerAssets;
using Xunit;

namespace SwordMaster.Application.Tests.PlayerAssets;

public sealed class PlayerEquipmentGrantAppServiceTests
{
    private static readonly Guid PlayerId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CharacterId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ExistingOwnedItemId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid GrantedOwnedItemId =
        Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid GrantId =
        Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid ConfigurationVersionId =
        Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid AdministratorId =
        Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid RollbackAdministratorId =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid GrantIdempotencyKey =
        Guid.Parse("88888888-8888-8888-8888-888888888888");
    private static readonly Guid RollbackIdempotencyKey =
        Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly DateTimeOffset OperationTime =
        new(2026, 8, 10, 1, 2, 3, TimeSpan.Zero);

    [Fact]
    public void PlayerEquipmentGrantFingerprintsUseFrozenCanonicalJson()
    {
        var service = new PlayerEquipmentGrantRequestFingerprint();
        const string grantJson =
            "{\"command\":\"grant\",\"playerId\":\"11111111-1111-1111-1111-111111111111\"," +
            "\"expectedRevision\":7,\"configuredItemId\":\"starter-sword\"," +
            "\"reason\":\"Support correction\"}";
        const string rollbackJson =
            "{\"command\":\"rollback\",\"playerId\":\"11111111-1111-1111-1111-111111111111\"," +
            "\"grantId\":\"55555555-5555-5555-5555-555555555555\"," +
            "\"expectedRevision\":8,\"reason\":\"Support rollback\"}";

        Assert.Equal(
            Hash(grantJson),
            service.CreateGrant(PlayerId, 7, "starter-sword", "Support correction"));
        Assert.Equal(
            Hash(rollbackJson),
            service.CreateRollback(PlayerId, GrantId, 8, "Support rollback"));
    }

    [Fact]
    public void PlayerEquipmentGrantPermissionIsDefinedAndRegisteredByContractsModule()
    {
        var context = new PermissionDefinitionContext();
        new PlayerEquipmentGrantPermissionDefinitionProvider().Define(context);

        var group = Assert.Single(context.Groups);
        Assert.Equal(PlayerEquipmentGrantPermissions.GroupName, group.Name);
        var permission = Assert.Single(group.Permissions);
        Assert.Equal(
            "SwordMaster.PlayerAssets.EquipmentGrants.Manage",
            permission.Name);
        Assert.Equal(PlayerEquipmentGrantPermissions.Manage, permission.Name);

        var services = new ServiceCollection();
        new SwordMasterApplicationContractsModule().ConfigureServices(
            services,
            configuration: null!);
        var registration = Assert.Single(
            services,
            descriptor => descriptor.ServiceType == typeof(IPermissionDefinitionProvider));
        Assert.Equal(
            typeof(PlayerEquipmentGrantPermissionDefinitionProvider),
            registration.ImplementationType);
        Assert.Equal(ServiceLifetime.Transient, registration.Lifetime);
    }

    [Fact]
    public async Task UnauthenticatedAdministratorIsRejectedBeforePermissionAndRepositoryAccess()
    {
        var fixture = CreateFixture(isAuthenticated: false);

        var exception = await Assert.ThrowsAsync<PlayerEquipmentGrantException>(() =>
            fixture.Service.GetPageAsync(PlayerId, 1, 20));

        Assert.Equal(
            PlayerEquipmentGrantErrorCodes.UnauthorizedAdministrator,
            exception.Code);
        Assert.Equal(PlayerEquipmentGrantFailureKind.Unauthorized, exception.FailureKind);
        Assert.Equal(0, fixture.PermissionChecker.CallCount);
        Assert.Equal(0, fixture.ProfileRepository.ReadCount);
        Assert.Equal(0, fixture.GrantRepository.CountCallCount);
    }

    [Fact]
    public async Task ForbiddenAdministratorIsRejectedBeforePlayerExistenceIsQueried()
    {
        var fixture = CreateFixture(permissionGranted: false);

        var exception = await Assert.ThrowsAsync<PlayerEquipmentGrantException>(() =>
            fixture.Service.GetPageAsync(PlayerId, 1, 20));

        Assert.Equal(PlayerEquipmentGrantErrorCodes.Forbidden, exception.Code);
        Assert.Equal(PlayerEquipmentGrantFailureKind.Forbidden, exception.FailureKind);
        Assert.Equal(PlayerEquipmentGrantPermissions.Manage, fixture.PermissionChecker.LastPermission);
        Assert.Equal(0, fixture.ProfileRepository.ReadCount);
        Assert.Equal(0, fixture.GrantRepository.CountCallCount);
    }

    [Theory]
    [InlineData(
        "grant",
        false,
        true,
        PlayerEquipmentGrantErrorCodes.UnauthorizedAdministrator,
        PlayerEquipmentGrantFailureKind.Unauthorized,
        0)]
    [InlineData(
        "grant",
        true,
        false,
        PlayerEquipmentGrantErrorCodes.Forbidden,
        PlayerEquipmentGrantFailureKind.Forbidden,
        1)]
    [InlineData(
        "rollback",
        false,
        true,
        PlayerEquipmentGrantErrorCodes.UnauthorizedAdministrator,
        PlayerEquipmentGrantFailureKind.Unauthorized,
        0)]
    [InlineData(
        "rollback",
        true,
        false,
        PlayerEquipmentGrantErrorCodes.Forbidden,
        PlayerEquipmentGrantFailureKind.Forbidden,
        1)]
    public async Task MutationAuthorizationFailsBeforeExistenceAndConfigurationAccess(
        string command,
        bool isAuthenticated,
        bool permissionGranted,
        string expectedCode,
        PlayerEquipmentGrantFailureKind expectedFailureKind,
        int expectedPermissionCalls)
    {
        var fixture = CreateFixture(
            isAuthenticated: isAuthenticated,
            permissionGranted: permissionGranted);
        Func<Task> action = command switch
        {
            "grant" => () => fixture.Service.GrantAsync(
                PlayerId,
                new GrantPlayerEquipmentRequest("starter-sword", "Support"),
                new PlayerEquipmentGrantMutationContext(1, GrantIdempotencyKey)),
            "rollback" => () => fixture.Service.RollbackAsync(
                PlayerId,
                GrantId,
                new RollbackPlayerEquipmentGrantRequest("Support"),
                new PlayerEquipmentGrantMutationContext(1, RollbackIdempotencyKey)),
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, null)
        };

        var exception = await Assert.ThrowsAsync<PlayerEquipmentGrantException>(action);

        Assert.Equal(expectedCode, exception.Code);
        Assert.Equal(expectedFailureKind, exception.FailureKind);
        Assert.Equal(expectedPermissionCalls, fixture.PermissionChecker.CallCount);
        Assert.Equal(0, fixture.DefinitionProvider.CallCount);
        Assert.Equal(0, fixture.ProfileRepository.ReadCount);
        Assert.Equal(0, fixture.ProfileRepository.WriteReadCount);
        Assert.Equal(0, fixture.GrantRepository.GrantReplayLookupCount);
        Assert.Equal(0, fixture.GrantRepository.RollbackReplayLookupCount);
        Assert.Equal(0, fixture.TransactionManager.BeginCount);
    }

    [Theory]
    [InlineData("page")]
    [InlineData("grant")]
    [InlineData("rollback")]
    public async Task MissingPlayerReturnsStableNotFoundForEveryOperation(string operation)
    {
        var fixture = CreateFixture(profileExists: false);
        Func<Task> action = operation switch
        {
            "page" => () => fixture.Service.GetPageAsync(PlayerId, 1, 20),
            "grant" => () => fixture.Service.GrantAsync(
                PlayerId,
                new GrantPlayerEquipmentRequest("starter-sword", "Support"),
                new PlayerEquipmentGrantMutationContext(1, GrantIdempotencyKey)),
            "rollback" => () => fixture.Service.RollbackAsync(
                PlayerId,
                GrantId,
                new RollbackPlayerEquipmentGrantRequest("Support"),
                new PlayerEquipmentGrantMutationContext(1, RollbackIdempotencyKey)),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
        };

        var exception = await Assert.ThrowsAsync<PlayerEquipmentGrantException>(action);

        Assert.Equal(PlayerEquipmentGrantErrorCodes.PlayerNotFound, exception.Code);
        Assert.Equal(PlayerEquipmentGrantFailureKind.NotFound, exception.FailureKind);
        Assert.Equal(0, fixture.GuidGenerator.CallCount);
        Assert.Empty(fixture.GrantRepository.Grants);
        Assert.Equal(0, fixture.TransactionManager.CompleteCount);
    }

    [Fact]
    public async Task GetPageReturnsCurrentRevisionAndDeterministicNewestFirstHistory()
    {
        var older = CreateGrant(
            grantId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            grantedAtUtc: OperationTime.UtcDateTime);
        var newer = CreateGrant(
            grantId: Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            ownedItemId: Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            idempotencyKey: Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            grantedAtUtc: OperationTime.AddMinutes(1).UtcDateTime);
        newer.Rollback(
            AdministratorId,
            OperationTime.AddMinutes(2).UtcDateTime,
            "Corrected",
            RollbackIdempotencyKey,
            new string('B', 64),
            rollbackResultingRevision: 4);
        var fixture = CreateFixture(grants: [older, newer]);

        var result = await fixture.Service.GetPageAsync(PlayerId, 1, 20);

        Assert.Equal(PlayerId, result.PlayerId);
        Assert.Equal(1, result.Revision);
        Assert.Equal(2, result.TotalCount);
        Assert.Equal([newer.GrantId, older.GrantId], result.Items.Select(item => item.GrantId));
        Assert.Equal(PlayerEquipmentGrantStatusDto.RolledBack, result.Items[0].Status);
        Assert.Equal(4, result.Items[0].RollbackResultingRevision);
        Assert.Equal("Corrected", result.Items[0].RollbackReason);
        Assert.Equal(PlayerEquipmentGrantStatusDto.Active, result.Items[1].Status);
    }

    [Theory]
    [InlineData("empty-player")]
    [InlineData("invalid-page")]
    [InlineData("invalid-page-size")]
    [InlineData("invalid-item")]
    [InlineData("invalid-reason")]
    [InlineData("invalid-context")]
    public async Task InvalidPlayerEquipmentGrantInputsFailBeforeAuthorization(string scenario)
    {
        var fixture = CreateFixture();
        Func<Task> action = scenario switch
        {
            "empty-player" => () => fixture.Service.GetPageAsync(Guid.Empty, 1, 20),
            "invalid-page" => () => fixture.Service.GetPageAsync(PlayerId, 0, 20),
            "invalid-page-size" => () => fixture.Service.GetPageAsync(PlayerId, 1, 101),
            "invalid-item" => () => fixture.Service.GrantAsync(
                PlayerId,
                new GrantPlayerEquipmentRequest("two words", "Support"),
                new PlayerEquipmentGrantMutationContext(1, GrantIdempotencyKey)),
            "invalid-reason" => () => fixture.Service.GrantAsync(
                PlayerId,
                new GrantPlayerEquipmentRequest("starter-sword", "   "),
                new PlayerEquipmentGrantMutationContext(1, GrantIdempotencyKey)),
            "invalid-context" => () => fixture.Service.GrantAsync(
                PlayerId,
                new GrantPlayerEquipmentRequest("starter-sword", "Support"),
                new PlayerEquipmentGrantMutationContext(0, Guid.Empty)),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null)
        };

        var exception = await Assert.ThrowsAsync<PlayerEquipmentGrantException>(action);

        Assert.Equal(PlayerEquipmentGrantErrorCodes.InvalidRequest, exception.Code);
        Assert.Equal(PlayerEquipmentGrantFailureKind.BadRequest, exception.FailureKind);
        Assert.Equal(0, fixture.PermissionChecker.CallCount);
        Assert.Equal(0, fixture.ProfileRepository.ReadCount);
        Assert.Equal(0, fixture.ProfileRepository.WriteReadCount);
    }

    [Fact]
    public async Task GrantCreatesOneOwnedItemAndDurableNormalizedEvidenceInOneTransaction()
    {
        var operationTime = OperationTime.AddTicks(7);
        var fixture = CreateFixture(operationTime: operationTime);

        var result = await fixture.Service.GrantAsync(
            PlayerId,
            new GrantPlayerEquipmentRequest(" starter-sword ", " Support correction "),
            new PlayerEquipmentGrantMutationContext(1, GrantIdempotencyKey));

        Assert.Equal(GrantId, result.GrantId);
        Assert.Equal(GrantedOwnedItemId, result.OwnedItemId);
        Assert.Equal("starter-sword", result.ConfiguredItemId);
        Assert.Equal(ConfigurationVersionId, result.ConfigurationVersionId);
        Assert.Equal(PlayerEquipmentGrantStatusDto.Active, result.Status);
        Assert.Equal(2, result.ResultingRevision);
        Assert.Equal("Support correction", result.GrantReason);
        Assert.Null(result.RolledBackAtUtc);
        Assert.Equal(2, fixture.Profile.Revision);
        Assert.Equal(2, fixture.Profile.OwnedItems.Count);
        var grant = Assert.Single(fixture.GrantRepository.Grants);
        Assert.Equal(
            new DateTime(
                operationTime.UtcDateTime.Ticks -
                    (operationTime.UtcDateTime.Ticks % TimeSpan.TicksPerMicrosecond),
                DateTimeKind.Utc),
            grant.GrantedAtUtc);
        Assert.Equal("127.0.0.1", grant.GrantIpAddress);
        Assert.Equal("operator-browser", grant.GrantUserAgent);
        Assert.Equal("correlation-1", grant.GrantCorrelationId);
        Assert.Equal(AdministratorId, grant.GrantedByAdministratorId);
        Assert.Equal(GrantIdempotencyKey, grant.GrantIdempotencyKey);
        Assert.Equal(
            new PlayerEquipmentGrantRequestFingerprint().CreateGrant(
                PlayerId,
                1,
                "starter-sword",
                "Support correction"),
            grant.GrantRequestFingerprint);
        Assert.Equal(2, fixture.GuidGenerator.CallCount);
        Assert.Equal(1, fixture.TransactionManager.BeginCount);
        Assert.Equal(1, fixture.TransactionManager.CompleteCount);
        Assert.Equal(1, fixture.TransactionManager.DisposeCount);
        Assert.Equal(2, fixture.GrantRepository.GrantReplayLookupCount);
    }

    [Fact]
    public async Task GrantReplayReturnsOriginalResultAfterRollbackWithoutCurrentDependencies()
    {
        var fingerprint = new PlayerEquipmentGrantRequestFingerprint().CreateGrant(
            PlayerId,
            1,
            "starter-sword",
            "Support correction");
        var grant = CreateGrant(fingerprint: fingerprint);
        grant.Rollback(
            AdministratorId,
            OperationTime.AddMinutes(1).UtcDateTime,
            "Later rollback",
            RollbackIdempotencyKey,
            new string('B', 64),
            rollbackResultingRevision: 3);
        var fixture = CreateFixture(grants: [grant]);

        var result = await fixture.Service.GrantAsync(
            PlayerId,
            new GrantPlayerEquipmentRequest("starter-sword", "Support correction"),
            new PlayerEquipmentGrantMutationContext(1, GrantIdempotencyKey));

        Assert.Equal(PlayerEquipmentGrantStatusDto.Active, result.Status);
        Assert.Equal(2, result.ResultingRevision);
        Assert.Equal(GrantId, result.GrantId);
        Assert.Equal(PlayerId, result.PlayerId);
        Assert.Equal(GrantedOwnedItemId, result.OwnedItemId);
        Assert.Equal("starter-sword", result.ConfiguredItemId);
        Assert.Equal(ConfigurationVersionId, result.ConfigurationVersionId);
        Assert.Equal(AdministratorId, result.GrantedByAdministratorId);
        Assert.Equal(OperationTime.UtcDateTime, result.GrantedAtUtc);
        Assert.Equal("Support correction", result.GrantReason);
        Assert.Null(result.RolledBackByAdministratorId);
        Assert.Null(result.RolledBackAtUtc);
        Assert.Null(result.RollbackReason);
        Assert.Equal(0, fixture.DefinitionProvider.CallCount);
        Assert.Equal(0, fixture.TransactionManager.BeginCount);
        Assert.Equal(0, fixture.GuidGenerator.CallCount);
        Assert.Equal(0, fixture.AuditContextAccessor.AccessCount);
    }

    [Fact]
    public async Task ChangedGrantFingerprintConflictsBeforeConfigurationAndTransaction()
    {
        var grant = CreateGrant(fingerprint: new string('A', 64));
        var fixture = CreateFixture(grants: [grant]);

        var exception = await Assert.ThrowsAsync<PlayerEquipmentGrantException>(() =>
            fixture.Service.GrantAsync(
                PlayerId,
                new GrantPlayerEquipmentRequest("starter-sword", "Different reason"),
                new PlayerEquipmentGrantMutationContext(1, GrantIdempotencyKey)));

        Assert.Equal(PlayerEquipmentGrantErrorCodes.IdempotencyConflict, exception.Code);
        Assert.Equal(PlayerEquipmentGrantFailureKind.Conflict, exception.FailureKind);
        Assert.Equal(0, fixture.DefinitionProvider.CallCount);
        Assert.Equal(0, fixture.TransactionManager.BeginCount);
    }

    [Theory]
    [InlineData("missing", PlayerEquipmentGrantErrorCodes.ConfigurationNotFound)]
    [InlineData("invalid", PlayerEquipmentGrantErrorCodes.ConfigurationInvalid)]
    [InlineData("mismatch", PlayerEquipmentGrantErrorCodes.ConfigurationInvalid)]
    public async Task ConfigurationFailuresDoNotOpenMutationTransaction(
        string scenario,
        string expectedCode)
    {
        var resolution = scenario switch
        {
            "missing" => EquipmentDefinitionResolution.Missing(),
            "invalid" => EquipmentDefinitionResolution.Invalid(),
            "mismatch" => EquipmentDefinitionResolution.Found(
                CreateDefinition("another-sword")),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null)
        };
        var fixture = CreateFixture(definitionResolution: resolution);

        var exception = await Assert.ThrowsAsync<PlayerEquipmentGrantException>(() =>
            fixture.Service.GrantAsync(
                PlayerId,
                new GrantPlayerEquipmentRequest("starter-sword", "Support"),
                new PlayerEquipmentGrantMutationContext(1, GrantIdempotencyKey)));

        Assert.Equal(expectedCode, exception.Code);
        Assert.Equal(PlayerEquipmentGrantFailureKind.UnprocessableEntity, exception.FailureKind);
        Assert.Equal(0, fixture.TransactionManager.BeginCount);
        Assert.Equal(0, fixture.GuidGenerator.CallCount);
        Assert.Single(fixture.Profile.OwnedItems);
    }

    [Fact]
    public async Task StaleGrantRevisionFailsBeforeIdsOrProfileMutation()
    {
        var fixture = CreateFixture();

        var exception = await Assert.ThrowsAsync<PlayerEquipmentGrantException>(() =>
            fixture.Service.GrantAsync(
                PlayerId,
                new GrantPlayerEquipmentRequest("starter-sword", "Support"),
                new PlayerEquipmentGrantMutationContext(2, GrantIdempotencyKey)));

        Assert.Equal(PlayerEquipmentGrantErrorCodes.ConcurrencyConflict, exception.Code);
        Assert.Equal(PlayerEquipmentGrantFailureKind.Conflict, exception.FailureKind);
        Assert.Equal(0, fixture.GuidGenerator.CallCount);
        Assert.Single(fixture.Profile.OwnedItems);
        Assert.Equal(1, fixture.Profile.Revision);
        Assert.Equal(0, fixture.TransactionManager.CompleteCount);
        Assert.Equal(1, fixture.TransactionManager.DisposeCount);
    }

    [Fact]
    public async Task GrantReplayInsideTransactionOccursAfterProfileLockAndCommitsReadOnlyScope()
    {
        var fingerprint = new PlayerEquipmentGrantRequestFingerprint().CreateGrant(
            PlayerId,
            1,
            "starter-sword",
            "Support");
        var replay = CreateGrant(fingerprint: fingerprint, reason: "Support");
        var fixture = CreateFixture();
        fixture.GrantRepository.GrantLookupOverride = call => call == 2 ? replay : null;

        var result = await fixture.Service.GrantAsync(
            PlayerId,
            new GrantPlayerEquipmentRequest("starter-sword", "Support"),
            new PlayerEquipmentGrantMutationContext(1, GrantIdempotencyKey));

        Assert.Equal(2, result.ResultingRevision);
        Assert.Equal(1, fixture.ProfileRepository.WriteReadCount);
        Assert.Equal(2, fixture.GrantRepository.GrantReplayLookupCount);
        Assert.Equal(1, fixture.TransactionManager.CompleteCount);
        Assert.Equal(0, fixture.GuidGenerator.CallCount);
        Assert.Single(fixture.Profile.OwnedItems);
    }

    [Fact]
    public async Task RollbackLocksProfileBeforeGrantAndRecordsOneCompensation()
    {
        var profile = CreateProfile(includeGrantedItem: true);
        var grant = CreateGrant(reason: "Original grant");
        var fixture = CreateFixture(profile: profile, grants: [grant]);

        var result = await fixture.Service.RollbackAsync(
            PlayerId,
            GrantId,
            new RollbackPlayerEquipmentGrantRequest(" Corrected by support "),
            new PlayerEquipmentGrantMutationContext(2, RollbackIdempotencyKey));

        Assert.Equal(PlayerEquipmentGrantStatusDto.RolledBack, result.Status);
        Assert.Equal(3, result.ResultingRevision);
        Assert.Equal("Corrected by support", result.RollbackReason);
        Assert.Equal(3, profile.Revision);
        Assert.DoesNotContain(
            profile.OwnedItems,
            item => item.OwnedItemId.Value == GrantedOwnedItemId);
        Assert.Equal(Domain.PlayerAssets.PlayerEquipmentGrantStatus.RolledBack, grant.Status);
        Assert.Equal(AdministratorId, grant.RolledBackByAdministratorId);
        Assert.Equal(OperationTime.UtcDateTime, grant.RolledBackAtUtc);
        Assert.Equal("Corrected by support", grant.RollbackReason);
        Assert.Equal(RollbackIdempotencyKey, grant.RollbackIdempotencyKey);
        Assert.Equal(
            new PlayerEquipmentGrantRequestFingerprint().CreateRollback(
                PlayerId,
                GrantId,
                2,
                "Corrected by support"),
            grant.RollbackRequestFingerprint);
        Assert.Equal("127.0.0.1", grant.RollbackIpAddress);
        Assert.Equal("operator-browser", grant.RollbackUserAgent);
        Assert.Equal("correlation-1", grant.RollbackCorrelationId);
        Assert.Equal(1, fixture.TransactionManager.CompleteCount);
        Assert.True(
            fixture.Events.IndexOf("profile.lock") < fixture.Events.IndexOf("grant.lock"));
    }

    [Fact]
    public async Task RollbackReplayReturnsOriginalCompensationBeforeTransaction()
    {
        var fingerprint = new PlayerEquipmentGrantRequestFingerprint().CreateRollback(
            PlayerId,
            GrantId,
            2,
            "Corrected");
        var grant = CreateGrant();
        grant.Rollback(
            RollbackAdministratorId,
            OperationTime.AddMinutes(1).UtcDateTime,
            "Corrected",
            RollbackIdempotencyKey,
            fingerprint,
            rollbackResultingRevision: 3);
        var fixture = CreateFixture(grants: [grant]);

        var result = await fixture.Service.RollbackAsync(
            PlayerId,
            GrantId,
            new RollbackPlayerEquipmentGrantRequest("Corrected"),
            new PlayerEquipmentGrantMutationContext(2, RollbackIdempotencyKey));

        Assert.Equal(3, result.ResultingRevision);
        Assert.Equal(PlayerEquipmentGrantStatusDto.RolledBack, result.Status);
        Assert.Equal(GrantId, result.GrantId);
        Assert.Equal(PlayerId, result.PlayerId);
        Assert.Equal(GrantedOwnedItemId, result.OwnedItemId);
        Assert.Equal("starter-sword", result.ConfiguredItemId);
        Assert.Equal(ConfigurationVersionId, result.ConfigurationVersionId);
        Assert.Equal(AdministratorId, result.GrantedByAdministratorId);
        Assert.Equal(OperationTime.UtcDateTime, result.GrantedAtUtc);
        Assert.Equal("Support correction", result.GrantReason);
        Assert.Equal(RollbackAdministratorId, result.RolledBackByAdministratorId);
        Assert.Equal(OperationTime.AddMinutes(1).UtcDateTime, result.RolledBackAtUtc);
        Assert.Equal("Corrected", result.RollbackReason);
        Assert.Equal(0, fixture.TransactionManager.BeginCount);
        Assert.Equal(0, fixture.ProfileRepository.WriteReadCount);
        Assert.Equal(0, fixture.AuditContextAccessor.AccessCount);
    }

    [Fact]
    public async Task RollbackReplayInsideTransactionReturnsExactEvidenceWithoutMutation()
    {
        var fingerprint = new PlayerEquipmentGrantRequestFingerprint().CreateRollback(
            PlayerId,
            GrantId,
            2,
            "Corrected");
        var grant = CreateGrant();
        grant.Rollback(
            RollbackAdministratorId,
            OperationTime.AddMinutes(1).UtcDateTime,
            "Corrected",
            RollbackIdempotencyKey,
            fingerprint,
            rollbackResultingRevision: 3);
        var fixture = CreateFixture(grants: [grant]);
        fixture.GrantRepository.RollbackLookupOverride = call =>
            call == 1 ? null : grant;

        var result = await fixture.Service.RollbackAsync(
            PlayerId,
            GrantId,
            new RollbackPlayerEquipmentGrantRequest("Corrected"),
            new PlayerEquipmentGrantMutationContext(2, RollbackIdempotencyKey));

        Assert.Equal(PlayerEquipmentGrantStatusDto.RolledBack, result.Status);
        Assert.Equal(3, result.ResultingRevision);
        Assert.Equal(RollbackAdministratorId, result.RolledBackByAdministratorId);
        Assert.Equal(OperationTime.AddMinutes(1).UtcDateTime, result.RolledBackAtUtc);
        Assert.Equal("Corrected", result.RollbackReason);
        Assert.Equal(2, fixture.GrantRepository.RollbackReplayLookupCount);
        Assert.Equal(["transaction.begin", "profile.lock", "grant.lock"], fixture.Events);
        Assert.Equal(1, fixture.TransactionManager.CompleteCount);
        Assert.Equal(1, fixture.TransactionManager.DisposeCount);
        Assert.Equal(1, fixture.Profile.Revision);
        Assert.Single(fixture.Profile.OwnedItems);
        Assert.Equal(0, fixture.AuditContextAccessor.AccessCount);
    }

    [Fact]
    public async Task StaleRollbackRevisionFailsBeforeProfileOrGrantMutation()
    {
        var profile = CreateProfile(includeGrantedItem: true);
        var grant = CreateGrant();
        var fixture = CreateFixture(profile: profile, grants: [grant]);

        var exception = await Assert.ThrowsAsync<PlayerEquipmentGrantException>(() =>
            fixture.Service.RollbackAsync(
                PlayerId,
                GrantId,
                new RollbackPlayerEquipmentGrantRequest("Corrected"),
                new PlayerEquipmentGrantMutationContext(1, RollbackIdempotencyKey)));

        Assert.Equal(PlayerEquipmentGrantErrorCodes.ConcurrencyConflict, exception.Code);
        Assert.Equal(PlayerEquipmentGrantFailureKind.Conflict, exception.FailureKind);
        Assert.Equal(2, profile.Revision);
        Assert.Equal(2, profile.OwnedItems.Count);
        Assert.Equal(Domain.PlayerAssets.PlayerEquipmentGrantStatus.Active, grant.Status);
        Assert.Null(grant.RolledBackAtUtc);
        Assert.Equal(0, fixture.TransactionManager.CompleteCount);
        Assert.Equal(1, fixture.TransactionManager.DisposeCount);
        Assert.Equal(0, fixture.AuditContextAccessor.AccessCount);
    }

    [Fact]
    public async Task ChangedRollbackFingerprintConflictsBeforeTransaction()
    {
        var grant = CreateGrant();
        grant.Rollback(
            AdministratorId,
            OperationTime.AddMinutes(1).UtcDateTime,
            "Original rollback",
            RollbackIdempotencyKey,
            new string('B', 64),
            rollbackResultingRevision: 3);
        var fixture = CreateFixture(grants: [grant]);

        var exception = await Assert.ThrowsAsync<PlayerEquipmentGrantException>(() =>
            fixture.Service.RollbackAsync(
                PlayerId,
                GrantId,
                new RollbackPlayerEquipmentGrantRequest("Different rollback"),
                new PlayerEquipmentGrantMutationContext(2, RollbackIdempotencyKey)));

        Assert.Equal(PlayerEquipmentGrantErrorCodes.IdempotencyConflict, exception.Code);
        Assert.Equal(PlayerEquipmentGrantFailureKind.Conflict, exception.FailureKind);
        Assert.Equal(0, fixture.TransactionManager.BeginCount);
    }

    [Fact]
    public async Task MissingGrantFailsAfterProfileLockWithoutProfileMutation()
    {
        var profile = CreateProfile(includeGrantedItem: true);
        var fixture = CreateFixture(profile: profile);

        var exception = await Assert.ThrowsAsync<PlayerEquipmentGrantException>(() =>
            fixture.Service.RollbackAsync(
                PlayerId,
                GrantId,
                new RollbackPlayerEquipmentGrantRequest("Corrected"),
                new PlayerEquipmentGrantMutationContext(2, RollbackIdempotencyKey)));

        Assert.Equal(PlayerEquipmentGrantErrorCodes.GrantNotFound, exception.Code);
        Assert.Equal(PlayerEquipmentGrantFailureKind.NotFound, exception.FailureKind);
        Assert.Equal(2, profile.Revision);
        Assert.Equal(2, profile.OwnedItems.Count);
        Assert.Equal(["transaction.begin", "profile.lock", "grant.lock"], fixture.Events);
        Assert.Equal(0, fixture.TransactionManager.CompleteCount);
    }

    [Fact]
    public async Task RollbackRechecksConflictingReceiptBeforeReportingMissingGrant()
    {
        var profile = CreateProfile(includeGrantedItem: true);
        var conflictingGrant = CreateGrant(
            grantId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        conflictingGrant.Rollback(
            AdministratorId,
            OperationTime.AddMinutes(1).UtcDateTime,
            "Another rollback",
            RollbackIdempotencyKey,
            new string('B', 64),
            rollbackResultingRevision: 3);
        var fixture = CreateFixture(profile: profile);
        fixture.GrantRepository.RollbackLookupOverride = call =>
            call == 1 ? null : conflictingGrant;

        var exception = await Assert.ThrowsAsync<PlayerEquipmentGrantException>(() =>
            fixture.Service.RollbackAsync(
                PlayerId,
                GrantId,
                new RollbackPlayerEquipmentGrantRequest("Corrected"),
                new PlayerEquipmentGrantMutationContext(2, RollbackIdempotencyKey)));

        Assert.Equal(PlayerEquipmentGrantErrorCodes.IdempotencyConflict, exception.Code);
        Assert.Equal(PlayerEquipmentGrantFailureKind.Conflict, exception.FailureKind);
        Assert.Equal(2, fixture.GrantRepository.RollbackReplayLookupCount);
        Assert.Equal(["transaction.begin", "profile.lock", "grant.lock"], fixture.Events);
        Assert.Equal(2, profile.Revision);
        Assert.Equal(0, fixture.TransactionManager.CompleteCount);
    }

    [Fact]
    public async Task AlreadyRolledBackGrantFailsBeforeRemovingOwnedItem()
    {
        var profile = CreateProfile(includeGrantedItem: true);
        var grant = CreateGrant();
        grant.Rollback(
            AdministratorId,
            OperationTime.AddMinutes(1).UtcDateTime,
            "Earlier rollback",
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            new string('B', 64),
            rollbackResultingRevision: 3);
        var fixture = CreateFixture(profile: profile, grants: [grant]);

        var exception = await Assert.ThrowsAsync<PlayerEquipmentGrantException>(() =>
            fixture.Service.RollbackAsync(
                PlayerId,
                GrantId,
                new RollbackPlayerEquipmentGrantRequest("Another rollback"),
                new PlayerEquipmentGrantMutationContext(2, RollbackIdempotencyKey)));

        Assert.Equal(PlayerEquipmentGrantErrorCodes.AlreadyRolledBack, exception.Code);
        Assert.Equal(PlayerEquipmentGrantFailureKind.Conflict, exception.FailureKind);
        Assert.Equal(2, profile.Revision);
        Assert.Equal(2, profile.OwnedItems.Count);
        Assert.Equal(0, fixture.TransactionManager.CompleteCount);
    }

    [Theory]
    [InlineData("equipped", PlayerEquipmentGrantErrorCodes.RollbackBlockedItemEquipped)]
    [InlineData("missing", PlayerEquipmentGrantErrorCodes.GrantedItemMissing)]
    public async Task RollbackProfileFailuresLeaveGrantActive(
        string scenario,
        string expectedCode)
    {
        var profile = CreateProfile(includeGrantedItem: scenario == "equipped");
        if (scenario == "equipped")
        {
            profile.Equip(
                profile.Revision,
                Domain.PlayerAssets.CharacterId.From(CharacterId),
                EquipmentSlot.Create("weapon"),
                OwnedItemId.From(GrantedOwnedItemId),
                CreateDefinition());
        }

        var grant = CreateGrant();
        var fixture = CreateFixture(profile: profile, grants: [grant]);

        var exception = await Assert.ThrowsAsync<PlayerEquipmentGrantException>(() =>
            fixture.Service.RollbackAsync(
                PlayerId,
                GrantId,
                new RollbackPlayerEquipmentGrantRequest("Corrected"),
                new PlayerEquipmentGrantMutationContext(
                    profile.Revision,
                    RollbackIdempotencyKey)));

        Assert.Equal(expectedCode, exception.Code);
        Assert.Equal(PlayerEquipmentGrantFailureKind.Conflict, exception.FailureKind);
        Assert.Equal(Domain.PlayerAssets.PlayerEquipmentGrantStatus.Active, grant.Status);
        Assert.Null(grant.RolledBackAtUtc);
        Assert.Equal(0, fixture.TransactionManager.CompleteCount);
    }

    private static TestFixture CreateFixture(
        bool isAuthenticated = true,
        bool permissionGranted = true,
        bool profileExists = true,
        EquipmentDefinitionResolution? definitionResolution = null,
        PlayerAssetsProfile? profile = null,
        IEnumerable<PlayerEquipmentGrant>? grants = null,
        DateTimeOffset? operationTime = null)
    {
        var events = new List<string>();
        var actualProfile = profile ?? CreateProfile();
        var currentAdministrator = new TestCurrentAdministrator(
            isAuthenticated,
            isAuthenticated ? AdministratorId : null);
        var permissionChecker = new RecordingPermissionChecker(permissionGranted);
        var auditContextAccessor = new RecordingAuditContextAccessor(
            new PlayerEquipmentGrantAuditContext(
                " 127.0.0.1 ",
                " operator-browser ",
                " correlation-1 "));
        var definitionProvider = new RecordingDefinitionProvider(
            definitionResolution ?? EquipmentDefinitionResolution.Found(CreateDefinition()));
        var profileRepository = new RecordingProfileRepository(
            profileExists ? actualProfile : null,
            events);
        var grantRepository = new RecordingGrantRepository(grants ?? [], events);
        var transactionManager = new RecordingTransactionManager(events);
        var guidGenerator = new SequenceGuidGenerator([GrantId, GrantedOwnedItemId]);
        var service = new PlayerEquipmentGrantAppService(
            currentAdministrator,
            permissionChecker,
            auditContextAccessor,
            definitionProvider,
            profileRepository,
            grantRepository,
            transactionManager,
            guidGenerator,
            new FixedTimeProvider(operationTime ?? OperationTime),
            new PlayerEquipmentGrantRequestFingerprint());
        return new TestFixture(
            service,
            actualProfile,
            permissionChecker,
            auditContextAccessor,
            definitionProvider,
            profileRepository,
            grantRepository,
            transactionManager,
            guidGenerator,
            events);
    }

    private static PlayerAssetsProfile CreateProfile(bool includeGrantedItem = false)
    {
        var profile = new PlayerAssetsProfile(
            Domain.PlayerAssets.PlayerId.From(PlayerId),
            new PlayerCharacter(
                Domain.PlayerAssets.CharacterId.From(CharacterId),
                [AttributeValue.Create("attack", 10)]),
            [
                new OwnedEquipmentItem(
                    OwnedItemId.From(ExistingOwnedItemId),
                    "training-sword")
            ]);
        if (includeGrantedItem)
        {
            profile.GrantEquipment(
                profile.Revision,
                OwnedItemId.From(GrantedOwnedItemId),
                "starter-sword");
        }

        return profile;
    }

    private static EquipmentDefinition CreateDefinition(
        string configuredItemId = "starter-sword")
    {
        return new EquipmentDefinition(
            configuredItemId,
            EquipmentSlot.Create("weapon"),
            ConfigurationVersionId,
            [AttributeValue.Create("attack", 5)]);
    }

    private static PlayerEquipmentGrant CreateGrant(
        Guid? grantId = null,
        Guid? ownedItemId = null,
        Guid? idempotencyKey = null,
        string? fingerprint = null,
        string reason = "Support correction",
        DateTime? grantedAtUtc = null)
    {
        return new PlayerEquipmentGrant(
            grantId ?? GrantId,
            Domain.PlayerAssets.PlayerId.From(PlayerId),
            OwnedItemId.From(ownedItemId ?? GrantedOwnedItemId),
            "starter-sword",
            ConfigurationVersionId,
            AdministratorId,
            grantedAtUtc ?? OperationTime.UtcDateTime,
            reason,
            idempotencyKey ?? GrantIdempotencyKey,
            fingerprint ?? new string('A', 64),
            grantResultingRevision: 2);
    }

    private static string Hash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private sealed record TestFixture(
        PlayerEquipmentGrantAppService Service,
        PlayerAssetsProfile Profile,
        RecordingPermissionChecker PermissionChecker,
        RecordingAuditContextAccessor AuditContextAccessor,
        RecordingDefinitionProvider DefinitionProvider,
        RecordingProfileRepository ProfileRepository,
        RecordingGrantRepository GrantRepository,
        RecordingTransactionManager TransactionManager,
        SequenceGuidGenerator GuidGenerator,
        List<string> Events);

    private sealed record TestCurrentAdministrator(
        bool IsAuthenticated,
        Guid? AdministratorId) : ICurrentSwordMasterAdministrator;

    private sealed class RecordingPermissionChecker(bool isGranted) : IPermissionChecker
    {
        public int CallCount { get; private set; }

        public string? LastPermission { get; private set; }

        public Task<bool> IsGrantedAsync(
            string permissionName,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastPermission = permissionName;
            return Task.FromResult(isGranted);
        }
    }

    private sealed class RecordingAuditContextAccessor(
        PlayerEquipmentGrantAuditContext context) : IPlayerEquipmentGrantAuditContextAccessor
    {
        public int AccessCount { get; private set; }

        public PlayerEquipmentGrantAuditContext Current
        {
            get
            {
                AccessCount++;
                return context;
            }
        }
    }

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

    private sealed class RecordingProfileRepository(
        PlayerAssetsProfile? profile,
        List<string> events) : IPlayerAssetsProfileRepository
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
            events.Add("profile.lock");
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

    private sealed class RecordingGrantRepository : IPlayerEquipmentGrantRepository
    {
        private readonly List<PlayerEquipmentGrant> _grants;
        private readonly List<string> _events;

        public RecordingGrantRepository(
            IEnumerable<PlayerEquipmentGrant> grants,
            List<string> events)
        {
            _grants = [.. grants];
            _events = events;
        }

        public IReadOnlyList<PlayerEquipmentGrant> Grants => _grants.AsReadOnly();

        public int CountCallCount { get; private set; }

        public int GrantReplayLookupCount { get; private set; }

        public int RollbackReplayLookupCount { get; private set; }

        public Func<int, PlayerEquipmentGrant?>? GrantLookupOverride { get; set; }

        public Func<int, PlayerEquipmentGrant?>? RollbackLookupOverride { get; set; }

        public Task<long> GetCountAsync(
            Domain.PlayerAssets.PlayerId playerId,
            CancellationToken cancellationToken = default)
        {
            CountCallCount++;
            return Task.FromResult((long)_grants.Count(grant => grant.PlayerId.Equals(playerId)));
        }

        public Task<IReadOnlyList<PlayerEquipmentGrant>> GetListAsync(
            Domain.PlayerAssets.PlayerId playerId,
            int skipCount,
            int maxResultCount,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<PlayerEquipmentGrant> result = _grants
                .Where(grant => grant.PlayerId.Equals(playerId))
                .Skip(skipCount)
                .Take(maxResultCount)
                .ToArray();
            return Task.FromResult(result);
        }

        public Task<PlayerEquipmentGrant?> FindByGrantIdempotencyKeyAsync(
            Domain.PlayerAssets.PlayerId playerId,
            Guid idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            GrantReplayLookupCount++;
            var overridden = GrantLookupOverride?.Invoke(GrantReplayLookupCount);
            var result = overridden ?? _grants.SingleOrDefault(grant =>
                grant.PlayerId.Equals(playerId) &&
                grant.GrantIdempotencyKey == idempotencyKey);
            return Task.FromResult(result);
        }

        public Task<PlayerEquipmentGrant?> FindByRollbackIdempotencyKeyAsync(
            Domain.PlayerAssets.PlayerId playerId,
            Guid idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            RollbackReplayLookupCount++;
            var result = RollbackLookupOverride is not null
                ? RollbackLookupOverride(RollbackReplayLookupCount)
                : _grants.SingleOrDefault(grant =>
                    grant.PlayerId.Equals(playerId) &&
                    grant.RollbackIdempotencyKey == idempotencyKey);
            return Task.FromResult(result);
        }

        public Task<PlayerEquipmentGrant?> FindByIdForUpdateAsync(
            Domain.PlayerAssets.PlayerId playerId,
            Guid grantId,
            CancellationToken cancellationToken = default)
        {
            _events.Add("grant.lock");
            var result = _grants.SingleOrDefault(grant =>
                grant.PlayerId.Equals(playerId) &&
                grant.GrantId == grantId);
            return Task.FromResult(result);
        }

        public Task AddAsync(
            PlayerEquipmentGrant grant,
            CancellationToken cancellationToken = default)
        {
            _grants.Add(grant);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingTransactionManager(
        List<string> events) : IPlayerAssetsTransactionManager
    {
        public int BeginCount { get; private set; }

        public int CompleteCount { get; private set; }

        public int DisposeCount { get; private set; }

        public Task<IPlayerAssetsTransactionScope> BeginAsync(
            CancellationToken cancellationToken = default)
        {
            BeginCount++;
            events.Add("transaction.begin");
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

    private sealed class SequenceGuidGenerator(IEnumerable<Guid> values) : IGuidGenerator
    {
        private readonly Queue<Guid> _values = new(values);

        public int CallCount { get; private set; }

        public Guid Create()
        {
            CallCount++;
            return _values.Dequeue();
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
