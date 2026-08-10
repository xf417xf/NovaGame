using System.Text.RegularExpressions;

namespace NovaGame.Domain.Shared.Content.GameConfig;

public static partial class GameConfigSchemaVersionRules
{
    public static bool IsValid(string? value)
    {
        return value is not null && SchemaVersionPattern().IsMatch(value);
    }

    [GeneratedRegex(
        "^[A-Za-z0-9][A-Za-z0-9._+-]{0,31}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex SchemaVersionPattern();
}
