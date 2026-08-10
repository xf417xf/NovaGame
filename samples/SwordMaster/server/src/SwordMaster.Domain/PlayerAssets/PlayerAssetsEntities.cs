using NovaCore.BuildingBlocks.Domain;

namespace SwordMaster.Domain.PlayerAssets;

public sealed class PlayerCharacter : Entity<Guid>
{
    private readonly List<AttributeValue> _baseAttributes = [];

    private PlayerCharacter()
    {
    }

    public PlayerCharacter(CharacterId characterId, IEnumerable<AttributeValue> baseAttributes)
    {
        ArgumentNullException.ThrowIfNull(characterId);
        ArgumentNullException.ThrowIfNull(baseAttributes);

        var copiedAttributes = baseAttributes
            .Select(attribute => attribute ?? throw new PlayerAssetsDomainException(
                PlayerAssetsErrorCodes.InvalidModifier,
                "Base attributes cannot contain null values."))
            .OrderBy(attribute => attribute.Code.Value, StringComparer.Ordinal)
            .ToArray();

        if (copiedAttributes
            .GroupBy(attribute => attribute.Code.Value, StringComparer.Ordinal)
            .Any(group => group.Count() > 1))
        {
            throw new PlayerAssetsDomainException(
                PlayerAssetsErrorCodes.InvalidModifier,
                "A character cannot contain duplicate base stat codes.");
        }

        Id = characterId.Value;
        _baseAttributes.AddRange(copiedAttributes);
    }

    public CharacterId CharacterId => global::SwordMaster.Domain.PlayerAssets.CharacterId.From(Id);

    public IReadOnlyCollection<AttributeValue> BaseAttributes => _baseAttributes.AsReadOnly();
}

public sealed class OwnedEquipmentItem : Entity<Guid>
{
    private OwnedEquipmentItem()
    {
    }

    public OwnedEquipmentItem(OwnedItemId ownedItemId, string configuredItemId)
    {
        ArgumentNullException.ThrowIfNull(ownedItemId);
        Id = ownedItemId.Value;
        ConfiguredItemId = EquipmentDefinition.NormalizeConfiguredItemId(configuredItemId);
    }

    public OwnedItemId OwnedItemId => global::SwordMaster.Domain.PlayerAssets.OwnedItemId.From(Id);

    public string ConfiguredItemId { get; private set; } = string.Empty;
}

public sealed class EquipmentAssignment : Entity<Guid>
{
    private EquipmentAssignment()
    {
        ModifierSnapshot = null!;
    }

    internal EquipmentAssignment(
        CharacterId characterId,
        EquipmentSlot slot,
        OwnedItemId ownedItemId,
        string configuredItemId,
        Guid configurationVersionId,
        EquipmentModifierSnapshot modifierSnapshot)
    {
        Id = Guid.NewGuid();
        CharacterId = characterId ?? throw new ArgumentNullException(nameof(characterId));
        Slot = slot ?? throw new ArgumentNullException(nameof(slot));
        OwnedItemId = ownedItemId ?? throw new ArgumentNullException(nameof(ownedItemId));
        ConfiguredItemId = EquipmentDefinition.NormalizeConfiguredItemId(configuredItemId);
        if (configurationVersionId == Guid.Empty)
        {
            throw new ArgumentException("Configuration-version identifiers cannot be empty.", nameof(configurationVersionId));
        }

        ConfigurationVersionId = configurationVersionId;
        ModifierSnapshot = modifierSnapshot?.Copy() ?? throw new ArgumentNullException(nameof(modifierSnapshot));
    }

    public CharacterId CharacterId { get; private set; } = null!;

    public EquipmentSlot Slot { get; private set; } = null!;

    public OwnedItemId OwnedItemId { get; private set; } = null!;

    public string ConfiguredItemId { get; private set; } = string.Empty;

    public Guid ConfigurationVersionId { get; private set; }

    public EquipmentModifierSnapshot ModifierSnapshot { get; private set; }

    public IReadOnlyList<AttributeValue> Modifiers => ModifierSnapshot.Modifiers;
}

public sealed class CharacterAttributeBreakdown
{
    internal CharacterAttributeBreakdown(
        IEnumerable<AttributeValue> baseAttributes,
        IEnumerable<AttributeValue> equipmentAttributes,
        IEnumerable<AttributeValue> effectiveAttributes)
    {
        BaseAttributes = Copy(baseAttributes);
        EquipmentAttributes = Copy(equipmentAttributes);
        EffectiveAttributes = Copy(effectiveAttributes);
    }

    public IReadOnlyList<AttributeValue> BaseAttributes { get; }

    public IReadOnlyList<AttributeValue> EquipmentAttributes { get; }

    public IReadOnlyList<AttributeValue> EffectiveAttributes { get; }

    private static IReadOnlyList<AttributeValue> Copy(IEnumerable<AttributeValue> attributes)
    {
        return Array.AsReadOnly(attributes.ToArray());
    }
}
