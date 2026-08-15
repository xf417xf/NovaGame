using System.ComponentModel.DataAnnotations;

namespace NovaGame.Application.Contracts.Operations.AdminAccess;

public sealed class ListAdministratorsRequest
{
    public string? Search { get; init; }

    public bool? IsActive { get; init; }

    public Guid? RoleId { get; init; }

    [Range(1, int.MaxValue)]
    public int Page { get; init; } = AdminAccessManagementContractLimits.DefaultPage;

    [Range(1, AdminAccessManagementContractLimits.MaximumPageSize)]
    public int PageSize { get; init; } = AdminAccessManagementContractLimits.DefaultPageSize;
}

public sealed class ListAdminRolesRequest
{
    public string? Search { get; init; }

    public bool? IsActive { get; init; }

    public bool? IsBuiltIn { get; init; }

    [Range(1, int.MaxValue)]
    public int Page { get; init; } = AdminAccessManagementContractLimits.DefaultPage;

    [Range(1, AdminAccessManagementContractLimits.MaximumPageSize)]
    public int PageSize { get; init; } = AdminAccessManagementContractLimits.DefaultPageSize;
}

public sealed class ListAdminSessionFamiliesRequest
{
    public Guid? AdministratorId { get; init; }

    public AdminSessionFamilyState? State { get; init; }

    [Range(1, int.MaxValue)]
    public int Page { get; init; } = AdminAccessManagementContractLimits.DefaultPage;

    [Range(1, AdminAccessManagementContractLimits.MaximumPageSize)]
    public int PageSize { get; init; } = AdminAccessManagementContractLimits.DefaultPageSize;
}

public sealed class ListAdminAccessAuditRecordsRequest
{
    public DateTime FromUtc { get; init; }

    public DateTime ToUtc { get; init; }

    public AdminAccessAuditActionDto? Action { get; init; }

    public bool? Succeeded { get; init; }

    public Guid? ActorAdministratorId { get; init; }

    public Guid? SubjectAdministratorId { get; init; }

    public Guid? RoleId { get; init; }

    public string? CorrelationId { get; init; }

    [Range(1, int.MaxValue)]
    public int Page { get; init; } = AdminAccessManagementContractLimits.DefaultPage;

    [Range(1, AdminAccessManagementContractLimits.MaximumPageSize)]
    public int PageSize { get; init; } = AdminAccessManagementContractLimits.DefaultPageSize;
}

public sealed class CreateAdministratorRequest
{
    [Required]
    public string UserName { get; init; } = string.Empty;

    [Required]
    public string DisplayName { get; init; } = string.Empty;

    [Required]
    [StringLength(
        AdminAccessManagementContractLimits.PasswordMaximumLength,
        MinimumLength = AdminAccessManagementContractLimits.PasswordMinimumLength)]
    public string InitialPassword { get; init; } = string.Empty;

    [Required]
    [MaxLength(AdminAccessManagementContractLimits.MaximumRoleIds)]
    public required IReadOnlyCollection<Guid> RoleIds { get; init; } = [];
}

public sealed class ChangeAdministratorDisplayNameRequest
{
    [Required]
    public string DisplayName { get; init; } = string.Empty;
}

public sealed class ReplaceAdministratorRolesRequest
{
    [Required]
    [MaxLength(AdminAccessManagementContractLimits.MaximumRoleIds)]
    public required IReadOnlyCollection<Guid> RoleIds { get; init; } = [];
}

public sealed class SetAdministratorActiveStateRequest
{
    public required bool IsActive { get; init; }
}

public sealed class ResetAdministratorPasswordRequest
{
    [Required]
    [StringLength(
        AdminAccessManagementContractLimits.PasswordMaximumLength,
        MinimumLength = AdminAccessManagementContractLimits.PasswordMinimumLength)]
    public string NewPassword { get; init; } = string.Empty;
}

public sealed class CreateAdminRoleRequest
{
    [Required]
    public string Name { get; init; } = string.Empty;

    [Required]
    [MaxLength(AdminAccessManagementContractLimits.MaximumPermissionNames)]
    public required IReadOnlyCollection<string> PermissionNames { get; init; } = [];
}

public sealed class RenameAdminRoleRequest
{
    [Required]
    public string Name { get; init; } = string.Empty;
}

public sealed class SetAdminRoleActiveStateRequest
{
    public required bool IsActive { get; init; }
}

public sealed class ReplaceAdminRolePermissionsRequest
{
    [Required]
    [MaxLength(AdminAccessManagementContractLimits.MaximumPermissionNames)]
    public required IReadOnlyCollection<string> PermissionNames { get; init; } = [];
}
