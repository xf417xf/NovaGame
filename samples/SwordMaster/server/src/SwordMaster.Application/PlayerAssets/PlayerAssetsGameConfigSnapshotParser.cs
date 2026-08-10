using System.Collections.ObjectModel;
using System.Text.Json;
using NovaGame.Application.Contracts.Content.GameConfig;
using NovaGame.Domain.Content.GameConfig;
using SwordMaster.Domain.PlayerAssets;

namespace SwordMaster.Application.PlayerAssets;

internal sealed record PlayerAssetsGameConfigSnapshot(
    Guid ConfigurationVersionId,
    IReadOnlyDictionary<string, EquipmentDefinition> EquipmentDefinitions);

internal sealed class PlayerAssetsGameConfigSnapshotParser
{
    private static readonly GameConfigSetKey ExpectedConfigurationSet =
        GameConfigSetKey.Create(PlayerAssetsGameConfigContract.ConfigurationSet);

    private static readonly string[] EquipmentDefinitionPropertyNames = ["id", "slot", "modifiers"];
    private static readonly string[] ModifierPropertyNames = ["statCode", "value"];

    private readonly GameConfigSnapshotValidator _snapshotValidator;

    public PlayerAssetsGameConfigSnapshotParser(GameConfigSnapshotValidator snapshotValidator)
    {
        _snapshotValidator = snapshotValidator ?? throw new ArgumentNullException(nameof(snapshotValidator));
    }

    public bool TryParse(
        GameConfigSnapshotDownload download,
        out PlayerAssetsGameConfigSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(download);
        snapshot = null!;
        if (download.Content is null ||
            !_snapshotValidator.TryValidate(
                download.Content,
                download.SnapshotHash,
                ExpectedConfigurationSet,
                PlayerAssetsGameConfigContract.SchemaVersion,
                expectedConfigurationVersionId: null,
                out var validated))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(
                validated.CanonicalContentJson,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 64
                });
            if (!TryParseDefinitions(
                    document.RootElement,
                    validated.ConfigurationVersionId,
                    out var definitions))
            {
                return false;
            }

            snapshot = new PlayerAssetsGameConfigSnapshot(
                validated.ConfigurationVersionId,
                new ReadOnlyDictionary<string, EquipmentDefinition>(definitions));
            return true;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or PlayerAssetsDomainException)
        {
            return false;
        }
    }

    private static bool TryParseDefinitions(
        JsonElement content,
        Guid configurationVersionId,
        out Dictionary<string, EquipmentDefinition> definitions)
    {
        definitions = new Dictionary<string, EquipmentDefinition>(StringComparer.Ordinal);
        if (!content.TryGetProperty("collections", out var collections) ||
            collections.ValueKind != JsonValueKind.Object ||
            !collections.TryGetProperty(
                PlayerAssetsGameConfigContract.EquipmentItemsCollection,
                out var equipmentItems) ||
            equipmentItems.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var item in equipmentItems.EnumerateArray())
        {
            if (!HasExactProperties(item, EquipmentDefinitionPropertyNames) ||
                !TryGetRequiredString(item, "id", out var configuredItemId) ||
                !TryGetRequiredString(item, "slot", out var slot) ||
                !item.TryGetProperty("modifiers", out var modifiersElement) ||
                modifiersElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var modifiers = new List<AttributeValue>();
            foreach (var modifier in modifiersElement.EnumerateArray())
            {
                if (!HasExactProperties(modifier, ModifierPropertyNames) ||
                    !TryGetRequiredString(modifier, "statCode", out var statCode) ||
                    !modifier.TryGetProperty("value", out var value) ||
                    value.ValueKind != JsonValueKind.Number ||
                    !value.TryGetInt32(out var modifierValue))
                {
                    return false;
                }

                modifiers.Add(AttributeValue.Create(statCode, modifierValue));
            }

            var definition = new EquipmentDefinition(
                configuredItemId,
                EquipmentSlot.Create(slot),
                configurationVersionId,
                modifiers);
            if (!definitions.TryAdd(definition.ConfiguredItemId, definition))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasExactProperties(JsonElement element, IReadOnlyCollection<string> expectedNames)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var actualNames = element
            .EnumerateObject()
            .Select(property => property.Name)
            .ToArray();
        return actualNames.Length == expectedNames.Count &&
            actualNames.All(expectedNames.Contains);
    }

    private static bool TryGetRequiredString(
        JsonElement element,
        string propertyName,
        out string value)
    {
        if (element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String &&
            property.GetString() is { Length: > 0 } propertyValue)
        {
            value = propertyValue;
            return true;
        }

        value = string.Empty;
        return false;
    }

}
