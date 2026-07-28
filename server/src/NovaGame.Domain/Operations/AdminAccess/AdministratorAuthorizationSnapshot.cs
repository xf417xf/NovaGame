namespace NovaGame.Domain.Operations.AdminAccess;

public sealed record AdministratorAuthorizationSnapshot(
    Guid AdministratorId,
    string UserName,
    string DisplayName,
    bool IsActive,
    IReadOnlyList<string> RoleNames,
    IReadOnlyList<string> PermissionNames);
