namespace SwordMaster.Domain.PlayerAssets;

public sealed class PlayerId : PlayerAssetsValueObject
{
    private PlayerId(Guid value)
    {
        Value = value;
    }

    public Guid Value { get; }

    public static PlayerId From(Guid value)
    {
        return new PlayerId(RequireIdentifier(value, nameof(value)));
    }

    protected override IEnumerable<object?> GetPlayerAssetsAtomicValues()
    {
        yield return Value;
    }

    private static Guid RequireIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Player identifiers cannot be empty.", parameterName);
        }

        return value;
    }
}

public sealed class CharacterId : PlayerAssetsValueObject
{
    private CharacterId(Guid value)
    {
        Value = value;
    }

    public Guid Value { get; }

    public static CharacterId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Character identifiers cannot be empty.", nameof(value));
        }

        return new CharacterId(value);
    }

    protected override IEnumerable<object?> GetPlayerAssetsAtomicValues()
    {
        yield return Value;
    }
}

public sealed class OwnedItemId : PlayerAssetsValueObject
{
    private OwnedItemId(Guid value)
    {
        Value = value;
    }

    public Guid Value { get; }

    public static OwnedItemId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Owned-item identifiers cannot be empty.", nameof(value));
        }

        return new OwnedItemId(value);
    }

    protected override IEnumerable<object?> GetPlayerAssetsAtomicValues()
    {
        yield return Value;
    }
}

public sealed class EquipmentSlot : PlayerAssetsValueObject
{
    public const int MaximumLength = 64;

    private EquipmentSlot(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static EquipmentSlot Create(string value)
    {
        var normalized = PlayerAssetsCodeRules.Normalize(value, MaximumLength);
        if (normalized is null ||
            normalized is "." or ".." ||
            normalized.Contains('/') ||
            normalized.Contains('\\'))
        {
            throw new ArgumentException(
                $"Equipment slots must contain 1 to {MaximumLength} non-whitespace, " +
                "non-control characters and cannot be URL dot segments or contain path separators.",
                nameof(value));
        }

        return new EquipmentSlot(normalized);
    }

    protected override IEnumerable<object?> GetPlayerAssetsAtomicValues()
    {
        yield return Value;
    }

    public override string ToString()
    {
        return Value;
    }
}

public sealed class StatCode : PlayerAssetsValueObject
{
    public const int MaximumLength = 64;

    private StatCode(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static StatCode Create(string value)
    {
        var normalized = PlayerAssetsCodeRules.Normalize(value, MaximumLength);
        if (normalized is null)
        {
            throw new ArgumentException(
                $"Stat codes must contain 1 to {MaximumLength} non-whitespace, non-control characters.",
                nameof(value));
        }

        return new StatCode(normalized);
    }

    protected override IEnumerable<object?> GetPlayerAssetsAtomicValues()
    {
        yield return Value;
    }

    public override string ToString()
    {
        return Value;
    }
}

public sealed class StatValue : PlayerAssetsValueObject
{
    private StatValue(int value)
    {
        Value = value;
    }

    public int Value { get; }

    public static StatValue From(int value)
    {
        return new StatValue(value);
    }

    protected override IEnumerable<object?> GetPlayerAssetsAtomicValues()
    {
        yield return Value;
    }

    public override string ToString()
    {
        return Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}

internal static class PlayerAssetsCodeRules
{
    public static string? Normalize(string? value, int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized) ||
            normalized.Length > maximumLength ||
            normalized.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)))
        {
            return null;
        }

        return normalized;
    }
}
