namespace NovaGame.Application.Contracts.Operations.AdminAccess;

public interface IAdminAccessAppService
{
    Task<AuthenticationSession> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default);

    Task<AuthenticationSession> RefreshAsync(
        string? refreshToken,
        CancellationToken cancellationToken = default);

    Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetPermissionCodesAsync(CancellationToken cancellationToken = default);

    Task<AdministratorInfoDto> GetCurrentAdministratorAsync(CancellationToken cancellationToken = default);
}
