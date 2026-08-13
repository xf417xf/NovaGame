using SwordMaster.Domain.PlayerAssets;
using Xunit;

namespace SwordMaster.Domain.Tests.PlayerAssets;

public sealed class PlayerEquipmentGrantTests
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
    private static readonly Guid GrantAdministratorId =
        Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid RollbackAdministratorId =
        Guid.Parse("88888888-8888-8888-8888-888888888888");
    private static readonly Guid GrantIdempotencyKey =
        Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly Guid RollbackIdempotencyKey =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime GrantedAtUtc =
        new(2026, 8, 10, 1, 2, 3, DateTimeKind.Utc);
    private static readonly string GrantFingerprint = new('A', 64);
    private static readonly string RollbackFingerprint = new('B', 64);

    [Fact]
    public void GrantEquipmentAddsOneUnequippedItemAndAdvancesRevisionOnce()
    {
        var profile = CreateProfile();

        var item = profile.GrantEquipment(
            expectedRevision: 1,
            OwnedItemId.From(GrantedOwnedItemId),
            " granted-sword ");

        Assert.Equal(2, profile.Revision);
        Assert.Equal(GrantedOwnedItemId, item.OwnedItemId.Value);
        Assert.Equal("granted-sword", item.ConfiguredItemId);
        Assert.Equal(2, profile.OwnedItems.Count);
        Assert.Empty(profile.EquipmentAssignments);
    }

    [Fact]
    public void GrantEquipmentRejectsStaleRevisionWithoutMutation()
    {
        var profile = CreateProfile();

        var exception = Assert.Throws<PlayerAssetsDomainException>(() =>
            profile.GrantEquipment(
                expectedRevision: 2,
                OwnedItemId.From(GrantedOwnedItemId),
                "granted-sword"));

        Assert.Equal(PlayerAssetsErrorCodes.ConcurrencyConflict, exception.Code);
        Assert.Equal(1, profile.Revision);
        Assert.Single(profile.OwnedItems);
    }

    [Fact]
    public void GrantEquipmentRejectsDuplicateOwnedItemWithoutMutation()
    {
        var profile = CreateProfile();

        Assert.Throws<ArgumentException>(() =>
            profile.GrantEquipment(
                expectedRevision: 1,
                OwnedItemId.From(ExistingOwnedItemId),
                "another-sword"));

        Assert.Equal(1, profile.Revision);
        var item = Assert.Single(profile.OwnedItems);
        Assert.Equal("training-sword", item.ConfiguredItemId);
    }

    [Fact]
    public void RollbackGrantedEquipmentRemovesExactUnequippedItemAndAdvancesRevisionOnce()
    {
        var profile = CreateProfile();
        profile.GrantEquipment(
            expectedRevision: 1,
            OwnedItemId.From(GrantedOwnedItemId),
            "granted-sword");

        var removed = profile.RollbackGrantedEquipment(
            expectedRevision: 2,
            OwnedItemId.From(GrantedOwnedItemId));

        Assert.Equal(3, profile.Revision);
        Assert.Equal(GrantedOwnedItemId, removed.OwnedItemId.Value);
        var retained = Assert.Single(profile.OwnedItems);
        Assert.Equal(ExistingOwnedItemId, retained.OwnedItemId.Value);
    }

    [Fact]
    public void RollbackGrantedEquipmentRejectsMissingIssuedItemWithoutMutation()
    {
        var profile = CreateProfile();

        var exception = Assert.Throws<PlayerAssetsDomainException>(() =>
            profile.RollbackGrantedEquipment(
                expectedRevision: 1,
                OwnedItemId.From(GrantedOwnedItemId)));

        Assert.Equal(
            PlayerAssetsErrorCodes.EquipmentGrantGrantedItemMissing,
            exception.Code);
        Assert.Equal(1, profile.Revision);
        Assert.Single(profile.OwnedItems);
    }

    [Fact]
    public void RollbackGrantedEquipmentRejectsEquippedIssuedItemWithoutMutation()
    {
        var profile = CreateProfile();
        profile.GrantEquipment(
            expectedRevision: 1,
            OwnedItemId.From(GrantedOwnedItemId),
            "granted-sword");
        profile.Equip(
            expectedRevision: 2,
            Domain.PlayerAssets.CharacterId.From(CharacterId),
            EquipmentSlot.Create("weapon"),
            OwnedItemId.From(GrantedOwnedItemId),
            new EquipmentDefinition(
                "granted-sword",
                EquipmentSlot.Create("weapon"),
                ConfigurationVersionId,
                [AttributeValue.Create("attack", 5)]));

        var exception = Assert.Throws<PlayerAssetsDomainException>(() =>
            profile.RollbackGrantedEquipment(
                expectedRevision: 3,
                OwnedItemId.From(GrantedOwnedItemId)));

        Assert.Equal(
            PlayerAssetsErrorCodes.EquipmentGrantRollbackBlockedItemEquipped,
            exception.Code);
        Assert.Equal(3, profile.Revision);
        Assert.Equal(2, profile.OwnedItems.Count);
        Assert.Single(profile.EquipmentAssignments);
    }

    [Fact]
    public void NewGrantCapturesNormalizedImmutableOriginalEvidence()
    {
        var grant = CreateGrant(
            reason: " Support correction ",
            ipAddress: " 127.0.0.1 ",
            userAgent: " operator-browser ",
            correlationId: " correlation-1 ");

        Assert.Equal(GrantId, grant.GrantId);
        Assert.Equal(PlayerId, grant.PlayerId.Value);
        Assert.Equal(GrantedOwnedItemId, grant.OwnedItemId.Value);
        Assert.Equal("granted-sword", grant.ConfiguredItemId);
        Assert.Equal(ConfigurationVersionId, grant.ConfigurationVersionId);
        Assert.Equal(PlayerEquipmentGrantStatus.Active, grant.Status);
        Assert.Equal(GrantAdministratorId, grant.GrantedByAdministratorId);
        Assert.Equal(GrantedAtUtc, grant.GrantedAtUtc);
        Assert.Equal("Support correction", grant.GrantReason);
        Assert.Equal(GrantIdempotencyKey, grant.GrantIdempotencyKey);
        Assert.Equal(GrantFingerprint, grant.GrantRequestFingerprint);
        Assert.Equal(2, grant.GrantResultingRevision);
        Assert.Equal("127.0.0.1", grant.GrantIpAddress);
        Assert.Equal("operator-browser", grant.GrantUserAgent);
        Assert.Equal("correlation-1", grant.GrantCorrelationId);
        Assert.Null(grant.RolledBackAtUtc);
        Assert.True(grant.MatchesGrantRequest(GrantFingerprint));
        Assert.False(grant.MatchesGrantRequest(RollbackFingerprint));
        Assert.False(grant.MatchesRollbackRequest(RollbackFingerprint));
    }

    [Fact]
    public void GrantNormalizesEmptyMetadataAndBoundsPersistedMetadata()
    {
        var grant = CreateGrant(
            ipAddress: "   ",
            userAgent: new string('u', PlayerEquipmentGrant.UserAgentMaximumLength + 10),
            correlationId: new string('c', PlayerEquipmentGrant.CorrelationIdMaximumLength + 10));

        Assert.Null(grant.GrantIpAddress);
        Assert.Equal(
            PlayerEquipmentGrant.UserAgentMaximumLength,
            grant.GrantUserAgent!.Length);
        Assert.Equal(
            PlayerEquipmentGrant.CorrelationIdMaximumLength,
            grant.GrantCorrelationId!.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("contains\ncontrol")]
    public void GrantRejectsInvalidReasonWithoutCreatingLifecycle(string reason)
    {
        Assert.Throws<ArgumentException>(() => CreateGrant(reason: reason));
    }

    [Fact]
    public void GrantRejectsReasonBeyondMaximumLengthAndInvalidConfiguredItem()
    {
        Assert.Throws<ArgumentException>(() => CreateGrant(
            reason: new string('r', PlayerEquipmentGrant.ReasonMaximumLength + 1)));
        Assert.Throws<ArgumentException>(() => CreateGrant(configuredItemId: "invalid item"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("GGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGG")]
    public void GrantRejectsNonCanonicalFingerprint(string fingerprint)
    {
        Assert.Throws<ArgumentException>(() => CreateGrant(fingerprint: fingerprint));
    }

    [Fact]
    public void GrantRejectsInvalidIdentityTimeAndRevisionInputs()
    {
        Assert.Throws<ArgumentException>(() => CreateGrant(grantId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => CreateGrant(configurationVersionId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => CreateGrant(administratorId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => CreateGrant(idempotencyKey: Guid.Empty));
        Assert.Throws<ArgumentException>(() => CreateGrant(
            grantedAtUtc: DateTime.SpecifyKind(GrantedAtUtc, DateTimeKind.Local)));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateGrant(resultingRevision: 0));
    }

    [Fact]
    public void RollbackRecordsOneCompleteImmutableCompensationResult()
    {
        var grant = CreateGrant();
        var rolledBackAtUtc = GrantedAtUtc.AddMinutes(1);

        grant.Rollback(
            RollbackAdministratorId,
            rolledBackAtUtc,
            " Corrected by support ",
            RollbackIdempotencyKey,
            RollbackFingerprint,
            rollbackResultingRevision: 3,
            ipAddress: " 10.0.0.1 ",
            userAgent: " rollback-browser ",
            correlationId: " rollback-correlation ");

        Assert.Equal(PlayerEquipmentGrantStatus.RolledBack, grant.Status);
        Assert.Equal(RollbackAdministratorId, grant.RolledBackByAdministratorId);
        Assert.Equal(rolledBackAtUtc, grant.RolledBackAtUtc);
        Assert.Equal("Corrected by support", grant.RollbackReason);
        Assert.Equal(RollbackIdempotencyKey, grant.RollbackIdempotencyKey);
        Assert.Equal(RollbackFingerprint, grant.RollbackRequestFingerprint);
        Assert.Equal(3, grant.RollbackResultingRevision);
        Assert.Equal("10.0.0.1", grant.RollbackIpAddress);
        Assert.Equal("rollback-browser", grant.RollbackUserAgent);
        Assert.Equal("rollback-correlation", grant.RollbackCorrelationId);
        Assert.True(grant.MatchesRollbackRequest(RollbackFingerprint));

        Assert.Equal(GrantAdministratorId, grant.GrantedByAdministratorId);
        Assert.Equal(GrantedAtUtc, grant.GrantedAtUtc);
        Assert.Equal("Original grant", grant.GrantReason);
        Assert.Equal(GrantFingerprint, grant.GrantRequestFingerprint);
        Assert.Equal(2, grant.GrantResultingRevision);
    }

    [Fact]
    public void RolledBackGrantRejectsAnotherLifecycleTransitionWithoutMutation()
    {
        var grant = CreateGrant();
        grant.Rollback(
            RollbackAdministratorId,
            GrantedAtUtc.AddMinutes(1),
            "First rollback",
            RollbackIdempotencyKey,
            RollbackFingerprint,
            rollbackResultingRevision: 3);

        var exception = Assert.Throws<PlayerAssetsDomainException>(() =>
            grant.Rollback(
                Guid.NewGuid(),
                GrantedAtUtc.AddMinutes(2),
                "Second rollback",
                Guid.NewGuid(),
                new string('C', 64),
                rollbackResultingRevision: 4));

        Assert.Equal(
            PlayerAssetsErrorCodes.EquipmentGrantAlreadyRolledBack,
            exception.Code);
        Assert.Equal(RollbackAdministratorId, grant.RolledBackByAdministratorId);
        Assert.Equal(3, grant.RollbackResultingRevision);
    }

    [Fact]
    public void RollbackRejectsInvalidEvidenceWithoutPartialLifecycleMutation()
    {
        var grant = CreateGrant();

        Assert.Throws<ArgumentException>(() =>
            grant.Rollback(
                RollbackAdministratorId,
                GrantedAtUtc.AddTicks(-1),
                "Rollback",
                RollbackIdempotencyKey,
                RollbackFingerprint,
                rollbackResultingRevision: 3));
        Assert.Throws<ArgumentException>(() =>
            grant.Rollback(
                Guid.Empty,
                GrantedAtUtc,
                "Rollback",
                RollbackIdempotencyKey,
                RollbackFingerprint,
                rollbackResultingRevision: 3));
        Assert.Throws<ArgumentException>(() =>
            grant.Rollback(
                RollbackAdministratorId,
                GrantedAtUtc,
                "   ",
                RollbackIdempotencyKey,
                RollbackFingerprint,
                rollbackResultingRevision: 3));
        Assert.Throws<ArgumentException>(() =>
            grant.Rollback(
                RollbackAdministratorId,
                GrantedAtUtc,
                "Rollback",
                Guid.Empty,
                RollbackFingerprint,
                rollbackResultingRevision: 3));
        Assert.Throws<ArgumentException>(() =>
            grant.Rollback(
                RollbackAdministratorId,
                GrantedAtUtc,
                "Rollback",
                RollbackIdempotencyKey,
                RollbackFingerprint.ToLowerInvariant(),
                rollbackResultingRevision: 3));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            grant.Rollback(
                RollbackAdministratorId,
                GrantedAtUtc,
                "Rollback",
                RollbackIdempotencyKey,
                RollbackFingerprint,
                rollbackResultingRevision: 0));

        Assert.Equal(PlayerEquipmentGrantStatus.Active, grant.Status);
        Assert.Null(grant.RolledBackByAdministratorId);
        Assert.Null(grant.RollbackReason);
        Assert.Null(grant.RollbackResultingRevision);
    }

    private static PlayerAssetsProfile CreateProfile()
    {
        return new PlayerAssetsProfile(
            Domain.PlayerAssets.PlayerId.From(PlayerId),
            new PlayerCharacter(
                Domain.PlayerAssets.CharacterId.From(CharacterId),
                [AttributeValue.Create("attack", 10)]),
            [
                new OwnedEquipmentItem(
                    OwnedItemId.From(ExistingOwnedItemId),
                    "training-sword")
            ]);
    }

    private static PlayerEquipmentGrant CreateGrant(
        Guid? grantId = null,
        Guid? configurationVersionId = null,
        Guid? administratorId = null,
        DateTime? grantedAtUtc = null,
        string reason = "Original grant",
        string configuredItemId = " granted-sword ",
        Guid? idempotencyKey = null,
        string? fingerprint = null,
        long resultingRevision = 2,
        string? ipAddress = null,
        string? userAgent = null,
        string? correlationId = null)
    {
        return new PlayerEquipmentGrant(
            grantId ?? GrantId,
            Domain.PlayerAssets.PlayerId.From(PlayerId),
            OwnedItemId.From(GrantedOwnedItemId),
            configuredItemId,
            configurationVersionId ?? ConfigurationVersionId,
            administratorId ?? GrantAdministratorId,
            grantedAtUtc ?? GrantedAtUtc,
            reason,
            idempotencyKey ?? GrantIdempotencyKey,
            fingerprint ?? GrantFingerprint,
            resultingRevision,
            ipAddress,
            userAgent,
            correlationId);
    }
}
