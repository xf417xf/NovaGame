using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NovaGame.Application.Contracts.Content.GameConfig;
using NovaGame.Domain.Content.GameConfig;
using SwordMaster.Application.PlayerAssets;
using SwordMaster.Domain.PlayerAssets;
using Xunit;

namespace SwordMaster.Application.Tests.PlayerAssets;

public sealed class GameConfigEquipmentDefinitionProviderTests
{
    private static readonly Guid FirstVersionId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid SecondVersionId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task FoundDefinitionUsesConfiguredEnvironmentAndSnapshotVersion()
    {
        var gameConfig = new StubGameConfigAppService();
        gameConfig.Enqueue(CreateDownload(
            FirstVersionId,
            Definitions(Definition("training-sword", "weapon", Modifier("attack", "7")))));
        var provider = CreateProvider(gameConfig, "acceptance");

        var resolution = await provider.ResolveActiveAsync("training-sword");

        Assert.Equal(EquipmentDefinitionResolutionStatus.Found, resolution.Status);
        var definition = Assert.IsType<EquipmentDefinition>(resolution.Definition);
        Assert.Equal("training-sword", definition.ConfiguredItemId);
        Assert.Equal("weapon", definition.CompatibleSlot.Value);
        Assert.Equal(FirstVersionId, definition.ConfigurationVersionId);
        var modifier = Assert.Single(definition.ModifierSnapshot.Modifiers);
        Assert.Equal("attack", modifier.Code.Value);
        Assert.Equal(7, modifier.Value.Value);
        var request = Assert.Single(gameConfig.ActiveSnapshotRequests);
        Assert.Equal(("acceptance", "swordmaster"), request);
    }

    [Fact]
    public async Task MissingDefinitionIsDistinctFromInvalidSnapshot()
    {
        var gameConfig = new StubGameConfigAppService();
        gameConfig.Enqueue(CreateDownload(
            FirstVersionId,
            Definitions(Definition("another-item", "weapon", Modifier("attack", "7")))));
        var provider = CreateProvider(gameConfig);

        var resolution = await provider.ResolveActiveAsync("training-sword");

        Assert.Equal(EquipmentDefinitionResolutionStatus.Missing, resolution.Status);
        Assert.Null(resolution.Definition);
    }

    [Fact]
    public async Task SnapshotHashMustMatchExactDownloadedBytes()
    {
        var valid = CreateDownload(
            FirstVersionId,
            Definitions(Definition("training-sword", "weapon", Modifier("attack", "7"))));
        var gameConfig = new StubGameConfigAppService();
        gameConfig.Enqueue(valid with { SnapshotHash = new string('0', 64) });

        var resolution = await CreateProvider(gameConfig).ResolveActiveAsync("training-sword");

        Assert.Equal(EquipmentDefinitionResolutionStatus.Invalid, resolution.Status);
        Assert.Null(resolution.Definition);
    }

    [Fact]
    public async Task EmbeddedContentHashMustMatchCanonicalContentBytes()
    {
        var gameConfig = new StubGameConfigAppService();
        gameConfig.Enqueue(CreateDownload(
            FirstVersionId,
            Definitions(Definition("training-sword", "weapon", Modifier("attack", "7"))),
            contentHashOverride: new string('0', 64)));

        var resolution = await CreateProvider(gameConfig).ResolveActiveAsync("training-sword");

        Assert.Equal(EquipmentDefinitionResolutionStatus.Invalid, resolution.Status);
    }

    [Theory]
    [InlineData("format")]
    [InlineData("configuration-set")]
    [InlineData("schema")]
    [InlineData("configuration-version")]
    [InlineData("created-at")]
    [InlineData("algorithm")]
    [InlineData("canonicalization")]
    public async Task SnapshotEnvelopeMustMatchApprovedContract(string invalidField)
    {
        var definitions = Definitions(
            Definition("training-sword", "weapon", Modifier("attack", "7")));
        var download = invalidField switch
        {
            "format" => CreateDownload(FirstVersionId, definitions, formatVersion: "2"),
            "configuration-set" => CreateDownload(
                FirstVersionId,
                definitions,
                configurationSet: "another-game"),
            "schema" => CreateDownload(FirstVersionId, definitions, schemaVersion: "another-schema.v1"),
            "configuration-version" => CreateDownload(Guid.Empty, definitions),
            "created-at" => CreateDownload(
                FirstVersionId,
                definitions,
                createdAtUtc: "2026-08-01T00:00:00"),
            "algorithm" => CreateDownload(FirstVersionId, definitions, algorithm: "SHA-512"),
            "canonicalization" => CreateDownload(
                FirstVersionId,
                definitions,
                canonicalization: "Another.Profile.v1"),
            _ => throw new ArgumentOutOfRangeException(nameof(invalidField), invalidField, null)
        };
        var gameConfig = new StubGameConfigAppService();
        gameConfig.Enqueue(download);

        var resolution = await CreateProvider(gameConfig).ResolveActiveAsync("training-sword");

        Assert.Equal(EquipmentDefinitionResolutionStatus.Invalid, resolution.Status);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("main/hand")]
    [InlineData("main\\hand")]
    public async Task InvalidSlotReturnsInvalidResolution(string slot)
    {
        var gameConfig = new StubGameConfigAppService();
        gameConfig.Enqueue(CreateDownload(
            FirstVersionId,
            Definitions(Definition("training-sword", slot, Modifier("attack", "7")))));

        var resolution = await CreateProvider(gameConfig).ResolveActiveAsync("training-sword");

        Assert.Equal(EquipmentDefinitionResolutionStatus.Invalid, resolution.Status);
    }

    [Fact]
    public async Task DuplicateEquipmentIdentifiersReturnInvalidResolution()
    {
        var duplicate = Definition("training-sword", "weapon", Modifier("attack", "7"));
        var gameConfig = new StubGameConfigAppService();
        gameConfig.Enqueue(CreateDownload(
            FirstVersionId,
            Definitions(duplicate, duplicate)));

        var resolution = await CreateProvider(gameConfig).ResolveActiveAsync("training-sword");

        Assert.Equal(EquipmentDefinitionResolutionStatus.Invalid, resolution.Status);
    }

    [Fact]
    public async Task DuplicateStatCodesReturnInvalidResolution()
    {
        var gameConfig = new StubGameConfigAppService();
        gameConfig.Enqueue(CreateDownload(
            FirstVersionId,
            Definitions(Definition(
                "training-sword",
                "weapon",
                Modifier("attack", "7"),
                Modifier("attack", "8")))));

        var resolution = await CreateProvider(gameConfig).ResolveActiveAsync("training-sword");

        Assert.Equal(EquipmentDefinitionResolutionStatus.Invalid, resolution.Status);
    }

    [Theory]
    [InlineData("\"7\"")]
    [InlineData("7.5")]
    [InlineData("2147483648")]
    [InlineData("-2147483649")]
    public async Task ModifierValuesMustBeExactInt32Numbers(string modifierJson)
    {
        var gameConfig = new StubGameConfigAppService();
        gameConfig.Enqueue(CreateDownload(
            FirstVersionId,
            Definitions(Definition("training-sword", "weapon", Modifier("attack", modifierJson)))));

        var resolution = await CreateProvider(gameConfig).ResolveActiveAsync("training-sword");

        Assert.Equal(EquipmentDefinitionResolutionStatus.Invalid, resolution.Status);
    }

    [Fact]
    public async Task ResolvedDefinitionRemainsPinnedWhenActiveSnapshotChanges()
    {
        var gameConfig = new StubGameConfigAppService();
        gameConfig.Enqueue(CreateDownload(
            FirstVersionId,
            Definitions(Definition("training-sword", "weapon", Modifier("attack", "7")))));
        gameConfig.Enqueue(CreateDownload(
            SecondVersionId,
            Definitions(Definition("training-sword", "weapon", Modifier("attack", "30")))));
        var provider = CreateProvider(gameConfig);

        var first = await provider.ResolveActiveAsync("training-sword");
        var second = await provider.ResolveActiveAsync("training-sword");

        var firstDefinition = Assert.IsType<EquipmentDefinition>(first.Definition);
        var secondDefinition = Assert.IsType<EquipmentDefinition>(second.Definition);
        Assert.Equal(FirstVersionId, firstDefinition.ConfigurationVersionId);
        Assert.Equal(7, Assert.Single(firstDefinition.ModifierSnapshot.Modifiers).Value.Value);
        Assert.Equal(SecondVersionId, secondDefinition.ConfigurationVersionId);
        Assert.Equal(30, Assert.Single(secondDefinition.ModifierSnapshot.Modifiers).Value.Value);
        Assert.Equal(FirstVersionId, firstDefinition.ConfigurationVersionId);
        Assert.Equal(7, Assert.Single(firstDefinition.ModifierSnapshot.Modifiers).Value.Value);
    }

    [Fact]
    public async Task GameConfigContractFailureReturnsInvalidResolution()
    {
        var gameConfig = new StubGameConfigAppService
        {
            ActiveSnapshotException = new GameConfigException(
                GameConfigFailureKind.NotFound,
                "NovaGame.GameConfig.ActiveVersionNotFound",
                "No active version exists.")
        };

        var resolution = await CreateProvider(gameConfig).ResolveActiveAsync("training-sword");

        Assert.Equal(EquipmentDefinitionResolutionStatus.Invalid, resolution.Status);
    }

    private static GameConfigEquipmentDefinitionProvider CreateProvider(
        StubGameConfigAppService gameConfig,
        string environment = "development")
    {
        return new GameConfigEquipmentDefinitionProvider(
            gameConfig,
            new PlayerAssetsGameConfigSnapshotParser(
                new GameConfigSnapshotValidator(new GameConfigContentProcessor())),
            Options.Create(new PlayerAssetsGameConfigOptions { Environment = environment }),
            NullLogger<GameConfigEquipmentDefinitionProvider>.Instance);
    }

    private static GameConfigSnapshotDownload CreateDownload(
        Guid configurationVersionId,
        string definitions,
        string formatVersion = "1",
        string configurationSet = "swordmaster",
        string schemaVersion = "swordmaster.player-assets.v1",
        string createdAtUtc = "2026-08-01T00:00:00.0000000Z",
        string algorithm = "SHA-256",
        string canonicalization = "NovaGame.GameConfig.CanonicalJson.v1",
        string? contentHashOverride = null)
    {
        var content = "{\"collections\":{\"equipment-items\":" + definitions + "}}";
        var contentHash = contentHashOverride ?? ComputeHash(Encoding.UTF8.GetBytes(content));
        var snapshotJson =
            "{\"formatVersion\":\"" + formatVersion +
            "\",\"schemaVersion\":\"" + schemaVersion +
            "\",\"configurationSet\":\"" + configurationSet +
            "\",\"configurationVersion\":\"" + configurationVersionId.ToString("D") +
            "\",\"createdAtUtc\":\"" + createdAtUtc +
            "\",\"integrity\":{\"algorithm\":\"" + algorithm +
            "\",\"canonicalization\":\"" + canonicalization +
            "\",\"contentHash\":\"" + contentHash +
            "\"},\"content\":" + content + "}";
        var bytes = Encoding.UTF8.GetBytes(snapshotJson);
        return new GameConfigSnapshotDownload(
            bytes,
            ComputeHash(bytes),
            "swordmaster-snapshot.json");
    }

    private static string Definitions(params string[] definitions)
    {
        return "[" + string.Join(',', definitions) + "]";
    }

    private static string Definition(
        string configuredItemId,
        string slot,
        params string[] modifiers)
    {
        return "{\"id\":" + JsonSerializer.Serialize(configuredItemId) +
            ",\"modifiers\":[" + string.Join(',', modifiers) +
            "],\"slot\":" + JsonSerializer.Serialize(slot) + "}";
    }

    private static string Modifier(string statCode, string valueJson)
    {
        return "{\"statCode\":\"" + statCode + "\",\"value\":" + valueJson + "}";
    }

    private static string ComputeHash(byte[] value)
    {
        return Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    }

    private sealed class StubGameConfigAppService : IGameConfigAppService
    {
        private readonly Queue<GameConfigSnapshotDownload> _downloads = new();

        public List<(string Environment, string ConfigurationSet)> ActiveSnapshotRequests { get; } = [];

        public GameConfigException? ActiveSnapshotException { get; init; }

        public void Enqueue(GameConfigSnapshotDownload download)
        {
            _downloads.Enqueue(download);
        }

        public Task<GameConfigSnapshotDownload> DownloadActiveSnapshotAsync(
            string environmentName,
            string configurationSet,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ActiveSnapshotRequests.Add((environmentName, configurationSet));
            if (ActiveSnapshotException is not null)
            {
                throw ActiveSnapshotException;
            }

            return Task.FromResult(_downloads.Dequeue());
        }

        public Task<GameConfigPageDto<GameConfigDraftSummaryDto>> ListDraftsAsync(
            ListGameConfigDraftsRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<GameConfigDraftDto> CreateDraftAsync(
            CreateGameConfigDraftRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<GameConfigDraftDto> GetDraftAsync(
            Guid draftId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<GameConfigDraftDto> UpdateDraftAsync(
            Guid draftId,
            UpdateGameConfigDraftRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<GameConfigValidationResultDto> ValidateDraftAsync(
            Guid draftId,
            ValidateGameConfigDraftRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<GameConfigVersionDto> PublishDraftAsync(
            Guid draftId,
            PublishGameConfigDraftRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<GameConfigPageDto<GameConfigVersionSummaryDto>> ListVersionsAsync(
            ListGameConfigVersionsRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<GameConfigVersionDto> GetVersionAsync(
            Guid versionId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<GameConfigSnapshotDownload> DownloadVersionSnapshotAsync(
            Guid versionId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<GameConfigEnvironmentDto> GetEnvironmentAsync(
            string environmentName,
            string configurationSet,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<GameConfigEnvironmentDto> ActivateVersionAsync(
            string environmentName,
            ActivateGameConfigVersionRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<GameConfigEnvironmentDto> RollbackEnvironmentAsync(
            string environmentName,
            RollbackGameConfigEnvironmentRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
