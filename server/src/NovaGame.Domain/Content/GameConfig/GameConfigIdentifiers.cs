using NovaCore.BuildingBlocks.Domain;
using NovaGame.Domain.Shared.Content.GameConfig;

namespace NovaGame.Domain.Content.GameConfig;

public sealed class GameConfigSetKey : ValueObject
{
    private GameConfigSetKey(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static GameConfigSetKey Create(string value)
    {
        if (!TryCreate(value, out var key))
        {
            throw new ArgumentException(
                $"Configuration-set keys must start with a lowercase letter and contain only lowercase letters, digits, or hyphens, up to {GameConfigConstants.ConfigurationSetKeyMaximumLength} characters.",
                nameof(value));
        }

        return key;
    }

    public static bool TryCreate(string? value, out GameConfigSetKey key)
    {
        var candidate = value?.Trim();
        if (!GameConfigSlugRules.IsValid(candidate, GameConfigConstants.ConfigurationSetKeyMaximumLength))
        {
            key = null!;
            return false;
        }

        key = new GameConfigSetKey(candidate!);
        return true;
    }

    protected override IEnumerable<object?> GetAtomicValues()
    {
        yield return Value;
    }
}

public sealed class GameConfigEnvironmentName : ValueObject
{
    private GameConfigEnvironmentName(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static GameConfigEnvironmentName Create(string value)
    {
        if (!TryCreate(value, out var name))
        {
            throw new ArgumentException(
                $"Environment names must start with a lowercase letter and contain only lowercase letters, digits, or hyphens, up to {GameConfigConstants.EnvironmentNameMaximumLength} characters.",
                nameof(value));
        }

        return name;
    }

    public static bool TryCreate(string? value, out GameConfigEnvironmentName name)
    {
        var candidate = value?.Trim();
        if (!GameConfigSlugRules.IsValid(candidate, GameConfigConstants.EnvironmentNameMaximumLength))
        {
            name = null!;
            return false;
        }

        name = new GameConfigEnvironmentName(candidate!);
        return true;
    }

    protected override IEnumerable<object?> GetAtomicValues()
    {
        yield return Value;
    }
}

internal static class GameConfigSlugRules
{
    public static bool IsValid(string? value, int maximumLength)
    {
        if (string.IsNullOrEmpty(value) ||
            value.Length > maximumLength ||
            !IsAsciiLowercaseLetter(value[0]))
        {
            return false;
        }

        return value.All(character =>
            IsAsciiLowercaseLetter(character) || character is >= '0' and <= '9' or '-');
    }

    private static bool IsAsciiLowercaseLetter(char character)
    {
        return character is >= 'a' and <= 'z';
    }
}
