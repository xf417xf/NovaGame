namespace NovaGame.Domain.Operations.AdminAccess;

public sealed record AdminAccessPageResult<T>(
    IReadOnlyList<T> Items,
    long TotalCount);

public sealed record AdminRoleReferenceSnapshot(
    Guid Id,
    string Name,
    bool IsBuiltIn,
    bool IsActive);

public sealed record AdministratorManagementSnapshot(
    Guid Id,
    string UserName,
    string DisplayName,
    bool IsActive,
    IReadOnlyList<AdminRoleReferenceSnapshot> Roles,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    DateTime? LastLoginAtUtc,
    long Revision);

public sealed record AdminRoleManagementSnapshot(
    Guid Id,
    string Name,
    bool IsBuiltIn,
    bool IsActive,
    IReadOnlyList<string> PermissionNames,
    long AdministratorCount,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    long Revision);

public enum AdminSessionFamilyStateSnapshot
{
    Active = 1,
    Expired = 2,
    Revoked = 3
}

public sealed record AdminSessionFamilySnapshot(
    Guid FamilyId,
    Guid AdministratorId,
    string UserName,
    string DisplayName,
    DateTime FirstCreatedAtUtc,
    DateTime LastActivityAtUtc,
    DateTime ExpiresAtUtc,
    string? LastIpAddress,
    AdminSessionFamilyStateSnapshot State,
    DateTime? RevokedAtUtc,
    RefreshTokenRevocationReason? RevocationReason);

public sealed record AdminAccessAuditQuery(
    DateTime FromUtc,
    DateTime ToUtc,
    AdminAccessAuditAction? Action,
    bool? Succeeded,
    Guid? ActorAdministratorId,
    Guid? SubjectAdministratorId,
    Guid? RoleId,
    string? CorrelationId);

public sealed record AdminAccessAuditRecordSnapshot(
    Guid Id,
    AdminAccessAuditAction Action,
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

public interface IAdminAccessManagementReadSnapshot
{
    Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);
}

public interface IAdminAccessManagementRepository
{
    Task<AdminAccessPageResult<AdministratorManagementSnapshot>> GetAdministratorPageAsync(
        string? normalizedUserNamePrefix,
        bool? isActive,
        Guid? roleId,
        int skipCount,
        int maxResultCount,
        CancellationToken cancellationToken = default);

    Task<AdministratorManagementSnapshot?> FindAdministratorSnapshotAsync(
        Guid administratorId,
        CancellationToken cancellationToken = default);

    Task<AdminAccessPageResult<AdminRoleManagementSnapshot>> GetRolePageAsync(
        string? normalizedNamePrefix,
        bool? isActive,
        bool? isBuiltIn,
        IReadOnlyCollection<string> registeredPermissionNames,
        int skipCount,
        int maxResultCount,
        CancellationToken cancellationToken = default);

    Task<AdminRoleManagementSnapshot?> FindRoleSnapshotAsync(
        Guid roleId,
        IReadOnlyCollection<string> registeredPermissionNames,
        CancellationToken cancellationToken = default);

    Task<AdminAccessPageResult<AdminSessionFamilySnapshot>> GetSessionFamilyPageAsync(
        Guid? administratorId,
        AdminSessionFamilyStateSnapshot? state,
        DateTime nowUtc,
        int skipCount,
        int maxResultCount,
        CancellationToken cancellationToken = default);

    Task<AdminAccessPageResult<AdminAccessAuditRecordSnapshot>> GetAuditPageAsync(
        AdminAccessAuditQuery query,
        int skipCount,
        int maxResultCount,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminRole>> GetBuiltInRolesForUpdateAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Guid>> GetAdministratorRoleIdsAsync(
        Guid administratorId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminRole>> GetRolesAsync(
        IReadOnlyCollection<Guid> roleIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Locks the built-in role first when present, then every remaining role by ordinal identifier.
    /// </summary>
    Task<IReadOnlyList<AdminRole>> GetRolesForUpdateInGlobalOrderAsync(
        IReadOnlyCollection<Guid> roleIds,
        CancellationToken cancellationToken = default);

    Task<AdminRole?> FindRoleForUpdateAsync(
        Guid roleId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RolePermission>> GetRolePermissionsForUpdateAsync(
        Guid roleId,
        CancellationToken cancellationToken = default);

    Task<AdminRole?> FindRoleByNormalizedNameForUpdateAsync(
        string normalizedName,
        CancellationToken cancellationToken = default);

    Task<AdminRole?> FindRoleByNormalizedNameAsync(
        string normalizedName,
        CancellationToken cancellationToken = default);

    Task<Administrator?> FindAdministratorForUpdateAsync(
        Guid administratorId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdministratorRole>> GetAdministratorRoleMembershipsForUpdateAsync(
        Guid administratorId,
        CancellationToken cancellationToken = default);

    Task<Administrator?> FindAdministratorByNormalizedUserNameForUpdateAsync(
        string normalizedUserName,
        CancellationToken cancellationToken = default);

    Task<int> CountActiveAdministratorsInRoleAsync(
        Guid roleId,
        CancellationToken cancellationToken = default);

    Task<long> CountAdministratorsInRoleAsync(
        Guid roleId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Guid>> GetAdministratorSessionFamilyIdsAsync(
        Guid administratorId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Guid>> GetSessionFamilyOwnerIdsAsync(
        Guid familyId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RefreshTokenSession>> GetSessionFamilyForUpdateAsync(
        Guid familyId,
        CancellationToken cancellationToken = default);

    Task AddAdministratorAsync(
        Administrator administrator,
        CancellationToken cancellationToken = default);

    Task AddRoleAsync(
        AdminRole role,
        CancellationToken cancellationToken = default);

    Task AddAuditAsync(
        AdminAccessAuditRecord auditRecord,
        CancellationToken cancellationToken = default);
}

public interface IAdminAccessCommandReceiptRepository
{
    Task<AdminAccessCommandReceipt> ClaimAsync(
        AdminAccessCommandReceipt candidate,
        CancellationToken cancellationToken = default);
}
