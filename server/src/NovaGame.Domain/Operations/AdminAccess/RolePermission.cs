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
