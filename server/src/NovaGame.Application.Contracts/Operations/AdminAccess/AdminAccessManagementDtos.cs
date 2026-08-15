namespace NovaGame.Application.Contracts.Operations.AdminAccess;

public sealed record AdminAccessPageDto<T>(
    IReadOnlyList<T> Items,
    long TotalCount,
    int Page,
    int PageSize);

public sealed record AdminRoleReferenceDto(
    Guid Id,
    string Name,
    bool IsBuiltIn,
    bool IsActive);

public sealed record AdministratorDto(
    Guid Id,
    string UserName,
    string DisplayName,
    bool IsActive,
    IReadOnlyList<AdminRoleReferenceDto> Roles,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    DateTime? LastLoginAtUtc,
    long Revision);

public sealed record AdminRoleSummaryDto(
    Guid Id,
    string Name,
    bool IsBuiltIn,
    bool IsActive,
    int PermissionCount,
    long AdministratorCount,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    long Revision);

public sealed record AdminRoleDto(
    Guid Id,
    string Name,
    bool IsBuiltIn,
    bool IsActive,
    int PermissionCount,
    long AdministratorCount,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    long Revision,
    IReadOnlyList<string> PermissionNames);

public sealed record PermissionGroupDto(
    string Name,
    string? DisplayName,
    IReadOnlyList<PermissionDefinitionDto> Permissions);

public sealed record PermissionDefinitionDto(
    string Name,
    string? DisplayName,
    string? Description);

public sealed record AdminSessionFamilyDto(
    Guid FamilyId,
    Guid AdministratorId,
    string UserName,
    string DisplayName,
    DateTime FirstCreatedAtUtc,
    DateTime LastActivityAtUtc,
    DateTime ExpiresAtUtc,
    string? LastIpAddress,
    AdminSessionFamilyState State,
    DateTime? RevokedAtUtc,
    AdminSessionRevocationReason? RevocationReason);

public sealed record AdminAccessAuditRecordDto(
    Guid Id,
    AdminAccessAuditActionDto Action,
    DateTime OccurredAtUtc,
    bool Succeeded,
    Guid? SubjectAdministratorId,
    Guid? ActorAdministratorId,
    Guid? RoleId,
    Guid? SessionFamilyId,
    string? SubjectUserName,
    string? PermissionName,
    string? ErrorCode,
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId,
    long? TargetRevision);
