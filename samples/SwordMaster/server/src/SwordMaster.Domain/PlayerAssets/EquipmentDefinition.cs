namespace SwordMaster.Domain.PlayerAssets;

public sealed class AttributeValue : PlayerAssetsValueObject
{
    public AttributeValue(StatCode code, StatValue value)
    {
        Code = code ?? throw new ArgumentNullException(nameof(code));
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public StatCode Code { get; }

    public StatValue Value { get; }

    public static AttributeValue Create(string code, int value)
    {
        return new AttributeValue(StatCode.Create(code), StatValue.From(value));
    }

    protected override IEnumerable<object?> GetPlayerAssetsAtomicValues()
    {
        yield return Code;
        yield return Value;
    }
}

public sealed class EquipmentModifierSnapshot : PlayerAssetsValueObject
{
    private readonly IReadOnlyList<AttributeValue> _modifiers;

    public EquipmentModifierSnapshot(IEnumerable<AttributeValue> modifiers)
    {
        ArgumentNullException.ThrowIfNull(modifiers);
        var copiedModifiers = modifiers
            .Select(modifier => modifier ?? throw new PlayerAssetsDomainException(
                PlayerAssetsErrorCodes.InvalidModifier,
                "Equipment modifiers cannot contain null values."))
            .OrderBy(modifier => modifier.Code.Value, StringComparer.Ordinal)
            .ToArray();

        if (copiedModifiers.Length == 0)
        {
            throw new PlayerAssetsDomainException(
                PlayerAssetsErrorCodes.InvalidModifier,
                "Equipment definitions require at least one modifier.");
        }

        if (copiedModifiers
            .GroupBy(modifier => modifier.Code.Value, StringComparer.Ordinal)
            .Any(group => group.Count() > 1))
        {
            throw new PlayerAssetsDomainException(
                PlayerAssetsErrorCodes.InvalidModifier,
                "Equipment definitions cannot contain duplicate stat codes.");
        }

        _modifiers = Array.AsReadOnly(copiedModifiers);
    }

    public IReadOnlyList<AttributeValue> Modifiers => _modifiers;

    internal EquipmentModifierSnapshot Copy()
    {
        return new EquipmentModifierSnapshot(_modifiers);
    }

    protected override IEnumerable<object?> GetPlayerAssetsAtomicValues()
    {
        foreach (var modifier in _modifiers)
        {
            yield return modifier;
        }
    }
}

public sealed class EquipmentDefinition
{
    public const int ConfiguredItemIdMaximumLength = 128;

    public EquipmentDefinition(
        string configuredItemId,
        EquipmentSlot compatibleSlot,
        Guid configurationVersionId,
        IEnumerable<AttributeValue> modifiers)
        : this(
            configuredItemId,
            compatibleSlot,
            configurationVersionId,
            new EquipmentModifierSnapshot(modifiers))
    {
    }

    public EquipmentDefinition(
        string configuredItemId,
        EquipmentSlot compatibleSlot,
        Guid configurationVersionId,
        EquipmentModifierSnapshot modifierSnapshot)
    {
        ConfiguredItemId = NormalizeConfiguredItemId(configuredItemId);
        CompatibleSlot = compatibleSlot ?? throw new ArgumentNullException(nameof(compatibleSlot));
        if (configurationVersionId == Guid.Empty)
        {
            throw new ArgumentException("Configuration-version identifiers cannot be empty.", nameof(configurationVersionId));
        }

        ConfigurationVersionId = configurationVersionId;
        ModifierSnapshot = modifierSnapshot?.Copy() ?? throw new ArgumentNullException(nameof(modifierSnapshot));
    }

    public string ConfiguredItemId { get; }

    public EquipmentSlot CompatibleSlot { get; }

    public Guid ConfigurationVersionId { get; }

    public EquipmentModifierSnapshot ModifierSnapshot { get; }

    internal static string NormalizeConfiguredItemId(string value)
    {
        var normalized = PlayerAssetsCodeRules.Normalize(value, ConfiguredItemIdMaximumLength);
        if (normalized is null)
        {
            throw new ArgumentException(
                $"Configured item identifiers must contain 1 to {ConfiguredItemIdMaximumLength} non-whitespace, non-control characters.",
                nameof(value));
        }

        return normalized;
    }
}
