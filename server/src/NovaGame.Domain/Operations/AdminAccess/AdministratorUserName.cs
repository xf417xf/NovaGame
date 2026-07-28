using System.Text;
using NovaCore.BuildingBlocks.Domain;
using NovaGame.Domain.Shared.Operations.AdminAccess;

namespace NovaGame.Domain.Operations.AdminAccess;

public sealed class AdministratorUserName : ValueObject
{
    public const int MinimumLength = AdminAccessConstants.UserNameMinimumLength;
    public const int MaximumLength = AdminAccessConstants.UserNameMaximumLength;

    private AdministratorUserName(string value, string normalizedValue)
    {
        Value = value;
        NormalizedValue = normalizedValue;
    }

    public string Value { get; }

    public string NormalizedValue { get; }

    public static AdministratorUserName Create(string value)
    {
        if (!TryCreate(value, out var userName))
        {
            throw new ArgumentException(
                $"Administrator user names must contain {MinimumLength} to {MaximumLength} non-control characters without whitespace.",
                nameof(value));
        }

        return userName;
    }

    public static bool TryCreate(string? value, out AdministratorUserName userName)
    {
        var candidate = value?.Trim().Normalize(NormalizationForm.FormKC);
        if (string.IsNullOrWhiteSpace(candidate) ||
            candidate.Length < MinimumLength ||
            candidate.Length > MaximumLength ||
            candidate.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)))
        {
            userName = null!;
            return false;
        }

        userName = new AdministratorUserName(candidate, candidate.ToUpperInvariant());
        return true;
    }

    protected override IEnumerable<object?> GetAtomicValues()
    {
        yield return NormalizedValue;
    }
}
