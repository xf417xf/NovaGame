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

        if (assignedByAdministratorId == Guid.Empty)
        {
            throw new ArgumentException(
                "The assigning administrator identifier cannot be empty.",
                nameof(assignedByAdministratorId));
        }

        Id = id;
        AdministratorId = administratorId;
        RoleId = roleId;
        AssignedAtUtc = RequireUtc(assignedAtUtc);
        AssignedByAdministratorId = assignedByAdministratorId;
    }

    public Guid AdministratorId { get; private set; }

    public Guid RoleId { get; private set; }

    public DateTime AssignedAtUtc { get; private set; }

    public Guid? AssignedByAdministratorId { get; private set; }

    private static DateTime RequireUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The assignment timestamp must use UTC.", nameof(value));
        }

        return value;
    }
}
