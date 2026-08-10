namespace SwordMaster.Application.PlayerAssets;

internal sealed class PlayerAssetsGameConfigOptions
{
    public const string SectionName = "SwordMaster:GameConfig";

    public string Environment { get; set; } = "development";

    public static bool IsValid(PlayerAssetsGameConfigOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var value = options.Environment;
        if (string.IsNullOrEmpty(value) || value.Length > 64 || !IsAsciiLowercaseLetter(value[0]))
        {
            return false;
        }

        return value.All(character =>
            IsAsciiLowercaseLetter(character) ||
            char.IsAsciiDigit(character) ||
            character == '-');
    }

    private static bool IsAsciiLowercaseLetter(char value)
    {
        return value is >= 'a' and <= 'z';
    }
}
