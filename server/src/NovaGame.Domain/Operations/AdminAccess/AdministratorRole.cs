using NovaCore.BuildingBlocks.Domain;

namespace NovaGame.Domain.Operations.AdminAccess;

public sealed class AdministratorRole : Entity<Guid>
{
    private AdministratorRole()
    {
    }

    internal AdministratorRole(
        Guid id,
        Guid administratorId,
        Guid roleId,
        DateTime assignedAtUtc,
        Guid? assignedByAdministratorId)
    {
        if (id == Guid.Empty || administratorId == Guid.Empty || roleId == Guid.Empty)
        {
            throw new ArgumentException("Role membership identifiers cannot be empty.");
        }

        Id = id;
        AdministratorId = administratorId;
        RoleId = roleId;
        AssignedAtUtc = assignedAtUtc;
        AssignedByAdministratorId = assignedByAdministratorId;
    }

    public Guid AdministratorId { get; private set; }

    public Guid RoleId { get; private set; }

    public DateTime AssignedAtUtc { get; private set; }

    public Guid? AssignedByAdministratorId { get; private set; }
}
