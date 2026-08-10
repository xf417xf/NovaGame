using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NovaGame.Application.Contracts.Content.GameConfig;

namespace SwordMaster.Application.PlayerAssets;

internal sealed class GameConfigEquipmentDefinitionProvider : IEquipmentDefinitionProvider
{
    private readonly IGameConfigAppService _gameConfigAppService;
    private readonly PlayerAssetsGameConfigSnapshotParser _parser;
    private readonly PlayerAssetsGameConfigOptions _options;
    private readonly ILogger<GameConfigEquipmentDefinitionProvider> _logger;

    public GameConfigEquipmentDefinitionProvider(
        IGameConfigAppService gameConfigAppService,
        PlayerAssetsGameConfigSnapshotParser parser,
        IOptions<PlayerAssetsGameConfigOptions> options,
        ILogger<GameConfigEquipmentDefinitionProvider> logger)
    {
        _gameConfigAppService = gameConfigAppService;
        _parser = parser;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<EquipmentDefinitionResolution> ResolveActiveAsync(
        string configuredItemId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(configuredItemId))
        {
            return EquipmentDefinitionResolution.Invalid();
        }

        GameConfigSnapshotDownload download;
        try
        {
            download = await _gameConfigAppService.DownloadActiveSnapshotAsync(
                _options.Environment,
                PlayerAssetsGameConfigContract.ConfigurationSet,
                cancellationToken);
        }
        catch (GameConfigException exception)
        {
            _logger.LogWarning(
                "Active SwordMaster equipment configuration could not be resolved because GameConfig returned {ErrorCode}.",
                exception.Code);
            return EquipmentDefinitionResolution.Invalid();
        }

        if (!_parser.TryParse(download, out var snapshot))
        {
            _logger.LogWarning(
                "Active SwordMaster equipment configuration snapshot {SnapshotHash} is invalid.",
                download.SnapshotHash);
            return EquipmentDefinitionResolution.Invalid();
        }

        var normalizedConfiguredItemId = configuredItemId.Trim();
        return snapshot.EquipmentDefinitions.TryGetValue(normalizedConfiguredItemId, out var definition)
            ? EquipmentDefinitionResolution.Found(definition)
            : EquipmentDefinitionResolution.Missing();
    }
}
