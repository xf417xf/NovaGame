using System.Globalization;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using NovaCore.BuildingBlocks.Authorization;
using NovaCore.BuildingBlocks.UnitOfWork;
using NovaCore.Shared.Results;
using NovaGame.Application.Contracts.Operations.AdminAccess;
using NovaGame.Domain.Shared.Operations.AdminAccess;

namespace NovaGame.Api.Operations.AdminAccess;

[ApiController]
[Route("api/admin-access")]
[NovaAuthorize(AdminAccessPermissions.Access)]
[UnitOfWork(IsDisabled = true)]
public sealed class AdminAccessManagementController : ControllerBase
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    private readonly IAdminAccessManagementAppService _managementAppService;

    public AdminAccessManagementController(IAdminAccessManagementAppService managementAppService)
    {
        _managementAppService = managementAppService;
    }

    [HttpGet("administrators")]
    [NovaAuthorize(AdminAccessPermissions.AdministratorsRead)]
    [ProducesResponseType(typeof(ApiResponse<AdminAccessPageDto<AdministratorDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<AdminAccessPageDto<AdministratorDto>>>> ListAdministratorsAsync(
        [FromQuery] ListAdministratorsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _managementAppService.ListAdministratorsAsync(request, cancellationToken);
        return Ok(ApiResponse<AdminAccessPageDto<AdministratorDto>>.Success(result));
    }

    [HttpPost("administrators")]
    [NovaAuthorize(AdminAccessPermissions.AdministratorsManage)]
    [ProducesResponseType(typeof(ApiResponse<AdministratorDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<AdministratorDto>>> CreateAdministratorAsync(
        [FromBody, Required] CreateAdministratorRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _managementAppService.CreateAdministratorAsync(
            request,
            ReadIdempotencyKey(),
            cancellationToken);
        SetEntityTag(result.Revision);
        return Ok(ApiResponse<AdministratorDto>.Success(result));
    }

    [HttpGet("administrators/{administratorId}")]
    [NovaAuthorize(AdminAccessPermissions.AdministratorsRead)]
    [ProducesResponseType(typeof(ApiResponse<AdministratorDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<AdministratorDto>>> GetAdministratorAsync(
        Guid administratorId,
        CancellationToken cancellationToken)
    {
        var result = await _managementAppService.GetAdministratorAsync(administratorId, cancellationToken);
        SetEntityTag(result.Revision);
        return Ok(ApiResponse<AdministratorDto>.Success(result));
    }

    [HttpPut("administrators/{administratorId}/display-name")]
    [NovaAuthorize(AdminAccessPermissions.AdministratorsManage)]
    [ProducesResponseType(typeof(ApiResponse<AdministratorDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<AdministratorDto>>> ChangeAdministratorDisplayNameAsync(
        Guid administratorId,
        [FromBody, Required] ChangeAdministratorDisplayNameRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _managementAppService.ChangeAdministratorDisplayNameAsync(
            administratorId,
            ReadExpectedRevision(),
            request,
            cancellationToken);
        SetEntityTag(result.Revision);
        return Ok(ApiResponse<AdministratorDto>.Success(result));
    }

    [HttpPut("administrators/{administratorId}/roles")]
    [NovaAuthorize(AdminAccessPermissions.AdministratorsManage)]
    [ProducesResponseType(typeof(ApiResponse<AdministratorDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<AdministratorDto>>> ReplaceAdministratorRolesAsync(
        Guid administratorId,
        [FromBody, Required] ReplaceAdministratorRolesRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _managementAppService.ReplaceAdministratorRolesAsync(
            administratorId,
            ReadExpectedRevision(),
            request,
            cancellationToken);
        SetEntityTag(result.Revision);
        return Ok(ApiResponse<AdministratorDto>.Success(result));
    }

    [HttpPut("administrators/{administratorId}/active-state")]
    [NovaAuthorize(AdminAccessPermissions.AdministratorsManage)]
    [ProducesResponseType(typeof(ApiResponse<AdministratorDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<AdministratorDto>>> SetAdministratorActiveStateAsync(
        Guid administratorId,
        [FromBody, Required] SetAdministratorActiveStateRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _managementAppService.SetAdministratorActiveStateAsync(
            administratorId,
            ReadExpectedRevision(),
            request,
            cancellationToken);
        SetEntityTag(result.Revision);
        return Ok(ApiResponse<AdministratorDto>.Success(result));
    }

    [HttpPost("administrators/{administratorId}/password-reset")]
    [NovaAuthorize(AdminAccessPermissions.AdministratorsResetPassword)]
    [ProducesResponseType(typeof(ApiResponse<AdministratorDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<AdministratorDto>>> ResetAdministratorPasswordAsync(
        Guid administratorId,
        [FromBody, Required] ResetAdministratorPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _managementAppService.ResetAdministratorPasswordAsync(
            administratorId,
            ReadExpectedRevision(),
            request,
            ReadIdempotencyKey(),
            cancellationToken);
        SetEntityTag(result.Revision);
        return Ok(ApiResponse<AdministratorDto>.Success(result));
    }

    [HttpGet("roles")]
    [NovaAuthorize(AdminAccessPermissions.RolesRead)]
    [ProducesResponseType(typeof(ApiResponse<AdminAccessPageDto<AdminRoleSummaryDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<AdminAccessPageDto<AdminRoleSummaryDto>>>> ListAdminRolesAsync(
        [FromQuery] ListAdminRolesRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _managementAppService.ListAdminRolesAsync(request, cancellationToken);
        return Ok(ApiResponse<AdminAccessPageDto<AdminRoleSummaryDto>>.Success(result));
    }

    [HttpPost("roles")]
    [NovaAuthorize(AdminAccessPermissions.RolesManage)]
    [ProducesResponseType(typeof(ApiResponse<AdminRoleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<AdminRoleDto>>> CreateAdminRoleAsync(
        [FromBody, Required] CreateAdminRoleRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _managementAppService.CreateAdminRoleAsync(
            request,
            ReadIdempotencyKey(),
            cancellationToken);
        SetEntityTag(result.Revision);
        return Ok(ApiResponse<AdminRoleDto>.Success(result));
    }

    [HttpGet("roles/{roleId}")]
    [NovaAuthorize(AdminAccessPermissions.RolesRead)]
    [ProducesResponseType(typeof(ApiResponse<AdminRoleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<AdminRoleDto>>> GetAdminRoleAsync(
        Guid roleId,
        CancellationToken cancellationToken)
    {
        var result = await _managementAppService.GetAdminRoleAsync(roleId, cancellationToken);
        SetEntityTag(result.Revision);
        return Ok(ApiResponse<AdminRoleDto>.Success(result));
    }

    [HttpPut("roles/{roleId}/name")]
    [NovaAuthorize(AdminAccessPermissions.RolesManage)]
    [ProducesResponseType(typeof(ApiResponse<AdminRoleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<AdminRoleDto>>> RenameAdminRoleAsync(
        Guid roleId,
        [FromBody, Required] RenameAdminRoleRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _managementAppService.RenameAdminRoleAsync(
            roleId,
            ReadExpectedRevision(),
            request,
            cancellationToken);
        SetEntityTag(result.Revision);
        return Ok(ApiResponse<AdminRoleDto>.Success(result));
    }

    [HttpPut("roles/{roleId}/active-state")]
    [NovaAuthorize(AdminAccessPermissions.RolesManage)]
    [ProducesResponseType(typeof(ApiResponse<AdminRoleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<AdminRoleDto>>> SetAdminRoleActiveStateAsync(
        Guid roleId,
        [FromBody, Required] SetAdminRoleActiveStateRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _managementAppService.SetAdminRoleActiveStateAsync(
            roleId,
            ReadExpectedRevision(),
            request,
            cancellationToken);
        SetEntityTag(result.Revision);
        return Ok(ApiResponse<AdminRoleDto>.Success(result));
    }

    [HttpPut("roles/{roleId}/permissions")]
    [NovaAuthorize(AdminAccessPermissions.RolesManage)]
    [ProducesResponseType(typeof(ApiResponse<AdminRoleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<AdminRoleDto>>> ReplaceAdminRolePermissionsAsync(
        Guid roleId,
        [FromBody, Required] ReplaceAdminRolePermissionsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _managementAppService.ReplaceAdminRolePermissionsAsync(
            roleId,
            ReadExpectedRevision(),
            request,
            cancellationToken);
        SetEntityTag(result.Revision);
        return Ok(ApiResponse<AdminRoleDto>.Success(result));
    }

    [HttpGet("permissions")]
    [NovaAuthorize(AdminAccessPermissions.RolesRead)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PermissionGroupDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PermissionGroupDto>>>> GetPermissionCatalogAsync(
        CancellationToken cancellationToken)
    {
        var result = await _managementAppService.GetPermissionCatalogAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<PermissionGroupDto>>.Success(result));
    }

    [HttpGet("sessions")]
    [NovaAuthorize(AdminAccessPermissions.SessionsRead)]
    [ProducesResponseType(typeof(ApiResponse<AdminAccessPageDto<AdminSessionFamilyDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<AdminAccessPageDto<AdminSessionFamilyDto>>>> ListSessionFamiliesAsync(
        [FromQuery] ListAdminSessionFamiliesRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _managementAppService.ListSessionFamiliesAsync(request, cancellationToken);
        return Ok(ApiResponse<AdminAccessPageDto<AdminSessionFamilyDto>>.Success(result));
    }

    [HttpPost("sessions/{familyId}/revoke")]
    [NovaAuthorize(AdminAccessPermissions.SessionsRevoke)]
    [ProducesResponseType(typeof(ApiResponse<AdminSessionFamilyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<AdminSessionFamilyDto>>> RevokeSessionFamilyAsync(
        Guid familyId,
        CancellationToken cancellationToken)
    {
        var result = await _managementAppService.RevokeSessionFamilyAsync(familyId, cancellationToken);
        return Ok(ApiResponse<AdminSessionFamilyDto>.Success(result));
    }

    [HttpGet("audit-records")]
    [NovaAuthorize(AdminAccessPermissions.AuditLogsRead)]
    [ProducesResponseType(typeof(ApiResponse<AdminAccessPageDto<AdminAccessAuditRecordDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<AdminAccessPageDto<AdminAccessAuditRecordDto>>>> ListAuditRecordsAsync(
        [FromQuery] ListAdminAccessAuditRecordsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _managementAppService.ListAuditRecordsAsync(request, cancellationToken);
        return Ok(ApiResponse<AdminAccessPageDto<AdminAccessAuditRecordDto>>.Success(result));
    }

    private Guid ReadIdempotencyKey()
    {
        var value = ReadExactlyOneHeader(IdempotencyKeyHeader);
        if (!Guid.TryParseExact(value, "D", out var key) ||
            !string.Equals(key.ToString("D"), value, StringComparison.Ordinal))
        {
            throw InvalidRequest($"{IdempotencyKeyHeader} must be a canonical GUID.");
        }

        return key;
    }

    private long ReadExpectedRevision()
    {
        var value = ReadExactlyOneHeader("If-Match");
        if (value.Length < 3 || value[0] != '\"' || value[^1] != '\"')
        {
            throw InvalidRequest("If-Match must contain one strong quoted revision ETag.");
        }

        var revisionText = value[1..^1];
        if (!long.TryParse(
                revisionText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var revision) ||
            revision <= 0 ||
            !string.Equals(
                revision.ToString(CultureInfo.InvariantCulture),
                revisionText,
                StringComparison.Ordinal))
        {
            throw InvalidRequest("If-Match must contain one strong quoted revision ETag.");
        }

        return revision;
    }

    private string ReadExactlyOneHeader(string name)
    {
        if (!Request.Headers.TryGetValue(name, out StringValues values) ||
            values.Count != 1)
        {
            throw InvalidRequest($"{name} must be supplied exactly once.");
        }

        var value = values[0];
        if (string.IsNullOrEmpty(value) || value.Contains(',', StringComparison.Ordinal))
        {
            throw InvalidRequest($"{name} must be supplied exactly once.");
        }

        return value;
    }

    private void SetEntityTag(long revision)
    {
        Response.Headers.ETag = $"\"{revision.ToString(CultureInfo.InvariantCulture)}\"";
    }

    private static AdminAccessManagementException InvalidRequest(string message)
    {
        return new AdminAccessManagementException(
            AdminAccessFailureKind.BadRequest,
            AdminAccessManagementErrorCodes.InvalidRequest,
            message);
    }
}
