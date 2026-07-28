namespace NovaGame.Application.Contracts.Operations.AdminAccess;

public sealed record AdministratorInfoDto(
    Guid Id,
    string UserName,
    string DisplayName,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> PermissionCodes);
