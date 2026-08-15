namespace NovaGame.Application.Contracts.Operations.AdminAccess;

public interface IAdminAccessManagementAppService
{
    Task<AdminAccessPageDto<AdministratorDto>> ListAdministratorsAsync(
        ListAdministratorsRequest request,
        CancellationToken cancellationToken = default);

    Task<AdministratorDto> CreateAdministratorAsync(
        CreateAdministratorRequest request,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<AdministratorDto> GetAdministratorAsync(
        Guid administratorId,
        CancellationToken cancellationToken = default);

    Task<AdministratorDto> ChangeAdministratorDisplayNameAsync(
        Guid administratorId,
        long expectedRevision,
        ChangeAdministratorDisplayNameRequest request,
        CancellationToken cancellationToken = default);

    Task<AdministratorDto> ReplaceAdministratorRolesAsync(
        Guid administratorId,
        long expectedRevision,
        ReplaceAdministratorRolesRequest request,
        CancellationToken cancellationToken = default);

    Task<AdministratorDto> SetAdministratorActiveStateAsync(
        Guid administratorId,
        long expectedRevision,
        SetAdministratorActiveStateRequest request,
        CancellationToken cancellationToken = default);

    Task<AdministratorDto> ResetAdministratorPasswordAsync(
        Guid administratorId,
        long expectedRevision,
        ResetAdministratorPasswordRequest request,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<AdminAccessPageDto<AdminRoleSummaryDto>> ListAdminRolesAsync(
        ListAdminRolesRequest request,
        CancellationToken cancellationToken = default);

    Task<AdminRoleDto> CreateAdminRoleAsync(
        CreateAdminRoleRequest request,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<AdminRoleDto> GetAdminRoleAsync(
        Guid roleId,
        CancellationToken cancellationToken = default);

    Task<AdminRoleDto> RenameAdminRoleAsync(
        Guid roleId,
        long expectedRevision,
        RenameAdminRoleRequest request,
        CancellationToken cancellationToken = default);

    Task<AdminRoleDto> SetAdminRoleActiveStateAsync(
        Guid roleId,
        long expectedRevision,
        SetAdminRoleActiveStateRequest request,
        CancellationToken cancellationToken = default);

    Task<AdminRoleDto> ReplaceAdminRolePermissionsAsync(
        Guid roleId,
        long expectedRevision,
        ReplaceAdminRolePermissionsRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PermissionGroupDto>> GetPermissionCatalogAsync(
        CancellationToken cancellationToken = default);

    Task<AdminAccessPageDto<AdminSessionFamilyDto>> ListSessionFamiliesAsync(
        ListAdminSessionFamiliesRequest request,
        CancellationToken cancellationToken = default);

    Task<AdminSessionFamilyDto> RevokeSessionFamilyAsync(
        Guid familyId,
        CancellationToken cancellationToken = default);

    Task<AdminAccessPageDto<AdminAccessAuditRecordDto>> ListAuditRecordsAsync(
        ListAdminAccessAuditRecordsRequest request,
        CancellationToken cancellationToken = default);
}
