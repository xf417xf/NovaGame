using System.Text;
using NovaCore.BuildingBlocks.Domain;

namespace NovaGame.Domain.Operations.AdminAccess;

public sealed class AdministratorDisplayName : ValueObject
{
    private AdministratorDisplayName(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static AdministratorDisplayName Create(string value)
    {
        string displayName;
        try
        {
            displayName = value?.Normalize(NormalizationForm.FormKC).Trim() ?? string.Empty;
        }
        catch (ArgumentException)
        {
            throw new ArgumentException(
                "Administrator display names must contain valid Unicode text.",
                nameof(value));
        }

        if (displayName.Length is < 1 or > 128 || displayName.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Administrator display names must contain 1 to 128 non-control characters.",
                nameof(value));
        }

        return new AdministratorDisplayName(displayName);
    }

    protected override IEnumerable<object?> GetAtomicValues()
    {
        yield return Value;
    }
}
