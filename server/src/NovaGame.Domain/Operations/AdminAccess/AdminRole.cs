using NovaCore.BuildingBlocks.Domain;

namespace NovaGame.Domain.Operations.AdminAccess;

public sealed class AdminRole : AggregateRoot<Guid>
{
    private readonly List<RolePermission> _permissions = [];

    private AdminRole()
    {
    }

    public AdminRole(Guid id, string name, DateTime createdAtUtc, bool isBuiltIn = false)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Role ID cannot be empty.", nameof(id));
        }

        var normalizedName = name?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName) || normalizedName.Length > 64)
        {
            throw new ArgumentException("Role names must contain 1 to 64 characters.", nameof(name));
        }

        Id = id;
        Name = normalizedName;
        NormalizedName = normalizedName.ToUpperInvariant();
        IsBuiltIn = isBuiltIn;
        CreatedAtUtc = RequireUtc(createdAtUtc, nameof(createdAtUtc));
    }

    public string Name { get; private set; } = string.Empty;

    public string NormalizedName { get; private set; } = string.Empty;

    public bool IsBuiltIn { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public IReadOnlyCollection<RolePermission> Permissions => _permissions.AsReadOnly();

    public RolePermission? GrantPermission(
        Guid grantId,
        string permissionName,
        DateTime grantedAtUtc,
        Guid? grantedByAdministratorId = null)
    {
        var normalizedPermissionName = permissionName?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedPermissionName) || normalizedPermissionName.Length > 128)
        {
            throw new ArgumentException("Permission names must contain 1 to 128 characters.", nameof(permissionName));
        }

        if (_permissions.Any(permission => permission.Name == normalizedPermissionName))
        {
            return null;
        }

        var permission = new RolePermission(
            grantId,
            Id,
            normalizedPermissionName,
            RequireUtc(grantedAtUtc, nameof(grantedAtUtc)),
            grantedByAdministratorId);
        _permissions.Add(permission);
        return permission;
    }

    private static DateTime RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The timestamp must use UTC.", parameterName);
        }

        return value;
    }
}
