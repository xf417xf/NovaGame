using System.Text;
using NovaCore.BuildingBlocks.Domain;
using NovaGame.Domain.Shared.Operations.AdminAccess;

namespace NovaGame.Domain.Operations.AdminAccess;

public sealed class AdminRoleName : ValueObject
{
    private AdminRoleName(string value, string normalizedValue)
    {
        Value = value;
        NormalizedValue = normalizedValue;
    }

    public string Value { get; }

    public string NormalizedValue { get; }

    public static AdminRoleName Create(string value)
    {
        string roleName;
        try
        {
            roleName = value?.Normalize(NormalizationForm.FormKC).Trim() ?? string.Empty;
        }
        catch (ArgumentException)
        {
            throw InvalidRequest("Role names must contain valid Unicode text.");
        }

        if (roleName.Length is < 1 or > 64 || roleName.Any(char.IsControl))
        {
            throw InvalidRequest("Role names must contain 1 to 64 non-control characters.");
        }

        var normalizedName = roleName.ToUpperInvariant();
        if (normalizedName.Length > 64)
        {
            throw InvalidRequest("Normalized role names cannot exceed 64 characters.");
        }

        return new AdminRoleName(roleName, normalizedName);
    }

    protected override IEnumerable<object?> GetAtomicValues()
    {
        yield return NormalizedValue;
    }

    private static AdminAccessManagementDomainException InvalidRequest(string message)
    {
        return new AdminAccessManagementDomainException(
            AdminAccessManagementErrorCodes.InvalidRequest,
            message);
    }
}
