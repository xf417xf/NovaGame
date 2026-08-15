using NovaCore.BuildingBlocks.Domain;

namespace NovaGame.Domain.Operations.AdminAccess;

public sealed class RolePermission : Entity<Guid>
{
    private RolePermission()
    {
    }

    internal RolePermission(
        Guid id,
        Guid roleId,
        string name,
        DateTime grantedAtUtc,
        Guid? grantedByAdministratorId)
    {
        if (id == Guid.Empty || roleId == Guid.Empty)
        {
            throw new ArgumentException("Permission grant identifiers cannot be empty.");
        }

        if (string.IsNullOrEmpty(name) || name.Length > 128 || name.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Permission names must contain 1 to 128 non-control characters.",
                nameof(name));
        }

        if (grantedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The timestamp must use UTC.", nameof(grantedAtUtc));
        }

        if (grantedByAdministratorId == Guid.Empty)
        {
            throw new ArgumentException(
                "The granting administrator identifier cannot be empty.",
                nameof(grantedByAdministratorId));
        }

        Id = id;
        RoleId = roleId;
        Name = name;
        GrantedAtUtc = grantedAtUtc;
        GrantedByAdministratorId = grantedByAdministratorId;
    }

    public Guid RoleId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public DateTime GrantedAtUtc { get; private set; }

    public Guid? GrantedByAdministratorId { get; private set; }
}
