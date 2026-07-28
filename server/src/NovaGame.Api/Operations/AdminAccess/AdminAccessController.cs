using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NovaCore.BuildingBlocks.Authorization;
using NovaCore.BuildingBlocks.UnitOfWork;
using NovaCore.Shared.Results;
using NovaGame.Application.Contracts.Operations.AdminAccess;
using NovaGame.Domain.Shared.Operations.AdminAccess;

namespace NovaGame.Api.Operations.AdminAccess;

[ApiController]
[Route("api/auth")]
[UnitOfWork(IsDisabled = true)]
public sealed class AdminAccessController : ControllerBase
{
    public const string RefreshTokenCookieName = "novagame.admin.refresh_token";

    private readonly IAdminAccessAppService _adminAccessAppService;

    public AdminAccessController(IAdminAccessAppService adminAccessAppService)
    {
        _adminAccessAppService = adminAccessAppService;
    }

    [AllowAnonymous]
    [EnableRateLimiting(AdminAccessApiServiceCollectionExtensions.LoginRateLimitPolicyName)]
    [HttpPost("login")]
    [ProducesResponseType(typeof(ApiResponse<AccessTokenResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ApiResponse<AccessTokenResponse>>> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var session = await _adminAccessAppService.LoginAsync(request, cancellationToken);
            SetRefreshTokenCookie(session);
            return Ok(ApiResponse<AccessTokenResponse>.Success(session.ToResponse()));
        }
        catch (AdminAccessException exception)
        {
            return Unauthorized(ApiResponse.Failure(exception.Code, exception.Message));
        }
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(ApiResponse<AccessTokenResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<AccessTokenResponse>>> RefreshAsync(
        CancellationToken cancellationToken)
    {
        Request.Cookies.TryGetValue(RefreshTokenCookieName, out var refreshToken);

        try
        {
            var session = await _adminAccessAppService.RefreshAsync(refreshToken, cancellationToken);
            SetRefreshTokenCookie(session);
            return Ok(ApiResponse<AccessTokenResponse>.Success(session.ToResponse()));
        }
        catch (AdminAccessException exception)
        {
            DeleteRefreshTokenCookie();
            return Unauthorized(ApiResponse.Failure(exception.Code, exception.Message));
        }
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse>> LogoutAsync(CancellationToken cancellationToken)
    {
        Request.Cookies.TryGetValue(RefreshTokenCookieName, out var refreshToken);
        await _adminAccessAppService.LogoutAsync(refreshToken, cancellationToken);
        DeleteRefreshTokenCookie();
        return Ok(ApiResponse.Success());
    }

    [NovaAuthorize(AdminAccessPermissions.Access)]
    [HttpGet("codes")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<string>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<string>>>> GetPermissionCodesAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var permissionCodes = await _adminAccessAppService.GetPermissionCodesAsync(cancellationToken);
            return Ok(ApiResponse<IReadOnlyList<string>>.Success(permissionCodes));
        }
        catch (AdminAccessException exception)
        {
            return Unauthorized(ApiResponse.Failure(exception.Code, exception.Message));
        }
    }

    [NovaAuthorize(AdminAccessPermissions.Access)]
    [HttpGet("/api/user/info")]
    [ProducesResponseType(typeof(ApiResponse<AdministratorInfoDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<AdministratorInfoDto>>> GetCurrentAdministratorAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var administrator = await _adminAccessAppService.GetCurrentAdministratorAsync(cancellationToken);
            return Ok(ApiResponse<AdministratorInfoDto>.Success(administrator));
        }
        catch (AdminAccessException exception)
        {
            return Unauthorized(ApiResponse.Failure(exception.Code, exception.Message));
        }
    }

    private void SetRefreshTokenCookie(AuthenticationSession session)
    {
        Response.Cookies.Append(
            RefreshTokenCookieName,
            session.RefreshToken,
            CreateRefreshTokenCookieOptions(session.RefreshTokenExpiresAtUtc));
    }

    private void DeleteRefreshTokenCookie()
    {
        Response.Cookies.Delete(
            RefreshTokenCookieName,
            CreateRefreshTokenCookieOptions(DateTime.UnixEpoch));
    }

    private static CookieOptions CreateRefreshTokenCookieOptions(DateTime expiresAtUtc)
    {
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            IsEssential = true,
            Path = "/api/auth",
            Expires = new DateTimeOffset(expiresAtUtc)
        };
    }
}
