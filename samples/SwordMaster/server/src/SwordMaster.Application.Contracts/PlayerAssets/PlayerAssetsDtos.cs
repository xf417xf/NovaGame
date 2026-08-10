namespace SwordMaster.Application.Contracts.PlayerAssets;

public sealed record PlayerAssetsMutationResultDto(long Revision);

public sealed record CharacterAttributeDto(
    string StatCode,
    int BaseValue,
    int EquipmentValue,
    int EffectiveValue);

public sealed record EquipmentModifierDto(
    string StatCode,
    int Value);

public sealed record EquipmentAssignmentDto(
    string Slot,
    Guid OwnedItemId,
    string ConfiguredItemId,
    Guid ConfigurationVersionId,
    IReadOnlyList<EquipmentModifierDto> Modifiers);

public sealed record CharacterAttributesDto(
    Guid CharacterId,
    long Revision,
    IReadOnlyList<CharacterAttributeDto> Attributes,
    IReadOnlyList<EquipmentAssignmentDto> EquipmentAssignments);
