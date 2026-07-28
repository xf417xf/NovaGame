using System.Security.Cryptography;
using System.Text;
using NovaCore.BuildingBlocks.Permissions;
using NovaGame.Domain.Content.GameConfig;
using NovaGame.Domain.Shared.Content.GameConfig;
using Xunit;

namespace NovaGame.EntityFrameworkCore.Tests;

public sealed class GameConfigDomainTests
{
    private static readonly Guid AdministratorId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime NowUtc = new(2026, 7, 25, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void SlugsRequireALowercaseLetterAndTheRestrictedAlphabet()
    {
        Assert.True(GameConfigSetKey.TryCreate("default-set2", out _));
        Assert.False(GameConfigSetKey.TryCreate("2default", out _));
        Assert.False(GameConfigSetKey.TryCreate("Default", out _));
        Assert.False(GameConfigSetKey.TryCreate("default_set", out _));
        Assert.False(GameConfigSetKey.TryCreate("default.set", out _));
    }

    [Fact]
    public void EveryDraftUpdateAdvancesRevisionAndClearsPersistedValidation()
    {
        var processor = new GameConfigContentProcessor();
        var invalidContent = """
            {
              "collections": {
                "records": [
                  { "id": "consumer", "target": { "$ref": "groups/missing" } }
                ]
              }
            }
            """;
        var draft = CreateDraft(invalidContent);
        var validation = processor.Process(draft.SchemaVersion, draft.RawContentJson).Validation;

        draft.RecordValidation(draft.Revision, validation, AdministratorId, NowUtc.AddMinutes(1));
        Assert.Equal(GameConfigValidationStatus.Invalid, draft.ValidationStatus);
        Assert.Contains(
            draft.ValidationIssues,
            issue => issue.Code == GameConfigValidationCodes.ReferenceNotFound);

        draft.Update(
            expectedRevision: 1,
            draft.Name,
            draft.SchemaVersion,
            draft.RawContentJson,
            AdministratorId,
            NowUtc.AddMinutes(2));

        Assert.Equal(2, draft.Revision);
        Assert.Equal(GameConfigValidationStatus.NotValidated, draft.ValidationStatus);
        Assert.Empty(draft.ValidationIssues);
        Assert.Null(draft.LastValidatedRevision);

        var staleUpdate = Assert.Throws<GameConfigDomainException>(() => draft.Update(
            expectedRevision: 1,
            draft.Name,
            draft.SchemaVersion,
            draft.RawContentJson,
            AdministratorId,
            NowUtc.AddMinutes(3)));
        Assert.Equal(GameConfigErrorCodes.DraftRevisionConflict, staleUpdate.Code);
    }

    [Fact]
    public void PublishedDraftCannotBeEditedOrValidatedAgain()
    {
        var processor = new GameConfigContentProcessor();
        var draft = CreateDraft(ValidContent);
        var validation = processor.Process(draft.SchemaVersion, draft.RawContentJson).Validation;
        var versionId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        draft.RecordValidation(1, validation, AdministratorId, NowUtc.AddMinutes(1));
        draft.MarkPublished(1, versionId);

        Assert.True(draft.IsPublished);
        Assert.Equal(versionId, draft.PublishedVersionId);
        var update = Assert.Throws<GameConfigDomainException>(() => draft.Update(
            1,
            "Renamed configuration",
            draft.SchemaVersion,
            draft.RawContentJson,
            AdministratorId,
            NowUtc.AddMinutes(2)));
        var validate = Assert.Throws<GameConfigDomainException>(() => draft.RecordValidation(
            1,
            validation,
            AdministratorId,
            NowUtc.AddMinutes(2)));
        var republish = Assert.Throws<GameConfigDomainException>(() => draft.MarkPublished(1, versionId));

        Assert.Equal(GameConfigErrorCodes.DraftRevisionAlreadyPublished, update.Code);
        Assert.Equal(GameConfigErrorCodes.DraftRevisionAlreadyPublished, validate.Code);
        Assert.Equal(GameConfigErrorCodes.DraftRevisionAlreadyPublished, republish.Code);
    }

    [Fact]
    public void ProcessorReturnsStableSchemaAndSemanticValidationCodes()
    {
        var processor = new GameConfigContentProcessor();

        Assert.Contains(
            processor.Process("1", "{").Validation.Issues,
            issue => issue.Code == GameConfigValidationCodes.InvalidJson);

        var invalid = processor.Process(
            "1",
            """
            {
              "collections": {
                "Records": [
                  { "id": "duplicate", "value": 1, "value": 2 },
                  { "id": "duplicate" },
                  { "id": "bad/ref" },
                  { "id": "consumer", "target": { "$ref": "missing/value" } },
                  { "id": "ambiguous", "target": { "$ref": "records/value", "extra": true } }
                ]
              }
            }
            """);

        Assert.False(invalid.Validation.IsValid);
        Assert.Null(invalid.Content);
        Assert.Contains(invalid.Validation.Issues, issue => issue.Code == GameConfigValidationCodes.InvalidCollectionName);
        Assert.Contains(invalid.Validation.Issues, issue => issue.Code == GameConfigValidationCodes.DuplicateProperty);
        Assert.Contains(invalid.Validation.Issues, issue => issue.Code == GameConfigValidationCodes.InvalidRecordId);
        Assert.Contains(invalid.Validation.Issues, issue => issue.Code == GameConfigValidationCodes.InvalidReference);
        Assert.Contains(invalid.Validation.Issues, issue => issue.Code == GameConfigValidationCodes.ReferenceNotFound);
    }

    [Fact]
    public void ProcessorRejectsDuplicateRecordIdentifiersWithinACollection()
    {
        var result = new GameConfigContentProcessor().Process(
            "1",
            """
            {
              "collections": {
                "records": [
                  { "id": "same" },
                  { "id": "same" }
                ]
              }
            }
            """);

        Assert.Contains(
            result.Validation.Issues,
            issue => issue.Code == GameConfigValidationCodes.DuplicateRecordId);
    }

    [Fact]
    public void CanonicalizationSortsCollectionsRecordsAndObjectProperties()
    {
        const string first = """
            {
              "collections": {
                "records": [
                  { "label": "Beta", "id": "beta" },
                  { "id": "alpha", "label": "Alpha" }
                ],
                "groups": [{ "sequence": ["b", "a"], "id": "primary", "weight": 2 }]
              }
            }
            """;
        const string second = """
            {"collections":{
              "groups":[{"weight":2,"id":"primary","sequence":["b","a"]}],
              "records":[
                {"label":"Alpha","id":"alpha"},
                {"id":"beta","label":"Beta"}
              ]
            }}
            """;
        var processor = new GameConfigContentProcessor();

        var firstResult = processor.Process("1", first);
        var secondResult = processor.Process("1", second);

        Assert.True(firstResult.Validation.IsValid);
        Assert.True(secondResult.Validation.IsValid);
        Assert.Equal(firstResult.Content!.CanonicalJson, secondResult.Content!.CanonicalJson);
        Assert.Equal(
            "{\"collections\":{\"groups\":[{\"id\":\"primary\",\"sequence\":[\"b\",\"a\"],\"weight\":2}],\"records\":[{\"id\":\"alpha\",\"label\":\"Alpha\"},{\"id\":\"beta\",\"label\":\"Beta\"}]}}",
            firstResult.Content.CanonicalJson);
    }

    [Fact]
    public void SnapshotHashesExactBytesAndPublishedVersionPersistsBothHashes()
    {
        var processor = new GameConfigContentProcessor();
        var processed = processor.Process("1", ValidContent).Content!;
        var builder = new GameConfigSnapshotBuilder();
        var versionId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var setKey = GameConfigSetKey.Create("default");

        var first = builder.Build(versionId, setKey, NowUtc, processed);
        var second = builder.Build(versionId, setKey, NowUtc, processed);
        var expectedSnapshotHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(first.Json)))
            .ToLowerInvariant();

        Assert.Equal(first.Json, second.Json);
        Assert.Equal(64, first.ContentHash.Length);
        Assert.Equal(expectedSnapshotHash, first.SnapshotHash);
        Assert.True(builder.Verify(first.Json, first.SnapshotHash));
        Assert.False(builder.Verify(first.Json + " ", first.SnapshotHash));

        var published = new PublishedGameConfigVersion(
            versionId,
            setKey,
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            sourceDraftRevision: 4,
            "Initial reusable configuration",
            processed,
            first,
            AdministratorId,
            NowUtc);

        Assert.Equal(first.ContentHash, published.ContentHash);
        Assert.Equal(first.SnapshotHash, published.SnapshotHash);
        Assert.Equal(first.Json, published.SnapshotJson);
    }

    [Fact]
    public void EnvironmentPersistsPreviousVersionAndReasonWhileRetriesRemainIdempotent()
    {
        var firstVersionId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var secondVersionId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var environment = new GameConfigEnvironment(
            Guid.NewGuid(),
            GameConfigSetKey.Create("default"),
            GameConfigEnvironmentName.Create("production"),
            firstVersionId,
            AdministratorId,
            NowUtc,
            "initial activation");

        var changed = environment.Activate(
            secondVersionId,
            1,
            AdministratorId,
            NowUtc.AddMinutes(1),
            "validated replacement");
        Assert.True(changed.Changed);
        Assert.Equal(firstVersionId, environment.PreviousVersionId);
        Assert.Equal("validated replacement", environment.Reason);
        Assert.Equal(2, environment.Revision);

        var retry = environment.Activate(
            secondVersionId,
            1,
            AdministratorId,
            NowUtc.AddMinutes(2),
            "ignored retry reason");
        Assert.False(retry.Changed);
        Assert.Equal(firstVersionId, retry.PreviousVersionId);
        Assert.Equal("validated replacement", retry.Reason);

        var conflict = Assert.Throws<GameConfigDomainException>(() => environment.Activate(
            Guid.NewGuid(),
            expectedRevision: 1,
            AdministratorId,
            NowUtc.AddMinutes(3)));
        Assert.Equal(GameConfigErrorCodes.ActivationConflict, conflict.Code);
    }

    [Fact]
    public void AuditFactoriesCaptureOptionalActivationAndRequiredRollbackReasons()
    {
        var setKey = GameConfigSetKey.Create("default");
        var environment = GameConfigEnvironmentName.Create("production");
        var previousVersionId = Guid.NewGuid();
        var targetVersionId = Guid.NewGuid();

        var activation = GameConfigAuditRecord.CreateActivation(
            Guid.NewGuid(),
            setKey,
            environment,
            previousVersionId,
            targetVersionId,
            AdministratorId,
            NowUtc,
            reason: "scheduled change");
        var rollback = GameConfigAuditRecord.CreateRollback(
            Guid.NewGuid(),
            setKey,
            environment,
            targetVersionId,
            previousVersionId,
            "Compatibility regression",
            AdministratorId,
            NowUtc,
            correlationId: "release-42");

        Assert.Equal("scheduled change", activation.Reason);
        Assert.Equal(GameConfigAuditAction.RolledBack, rollback.Action);
        Assert.Equal(targetVersionId, rollback.PreviousVersionId);
        Assert.Equal(previousVersionId, rollback.TargetVersionId);
        Assert.Throws<ArgumentException>(() => GameConfigAuditRecord.CreateRollback(
            Guid.NewGuid(),
            setKey,
            environment,
            targetVersionId,
            previousVersionId,
            " ",
            AdministratorId,
            NowUtc));
    }

    [Fact]
    public void PermissionProviderDefinesTheFourM05Capabilities()
    {
        var context = new PermissionDefinitionContext();

        new GameConfigPermissionDefinitionProvider().Define(context);

        var group = Assert.Single(context.Groups);
        Assert.Equal(GameConfigPermissions.GroupName, group.Name);
        Assert.Equal(
            new[]
            {
                GameConfigPermissions.Access,
                GameConfigPermissions.ManageDrafts,
                GameConfigPermissions.Publish,
                GameConfigPermissions.Activate
            },
            group.Permissions.Select(permission => permission.Name));
    }

    private static GameConfigDraft CreateDraft(string content)
    {
        return new GameConfigDraft(
            Guid.NewGuid(),
            GameConfigSetKey.Create("default"),
            "Default configuration",
            baseVersionId: null,
            GameConfigConstants.SupportedSchemaVersion,
            content,
            AdministratorId,
            NowUtc);
    }

    private const string ValidContent = """
        {
          "collections": {
            "groups": [{ "id": "base" }],
            "records": [
              {
                "id": "entry-a",
                "group": { "$ref": "groups/base" }
              }
            ]
          }
        }
        """;
}
