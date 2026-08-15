namespace NovaGame.Domain.Operations.AdminAccess;

public readonly record struct AdministratorRoleTarget(Guid MembershipId, Guid RoleId);

public sealed record AdministratorTransition(bool Changed, long Revision);

public sealed record AdministratorRoleReplacementTransition(
    bool Changed,
    long Revision,
    IReadOnlyList<Guid> AddedRoleIds,
    IReadOnlyList<Guid> RemovedRoleIds);
