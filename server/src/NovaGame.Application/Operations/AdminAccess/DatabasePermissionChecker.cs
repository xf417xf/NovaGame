using NovaCore.BuildingBlocks.CurrentUser;
using NovaCore.BuildingBlocks.Permissions;
using NovaGame.Domain.Operations.AdminAccess;

namespace NovaGame.Application.Operations.AdminAccess;

public sealed class DatabasePermissionChecker : IPermissionChecker
{
    private readonly ICurrentUser _currentUser;
    private readonly IPermissionDefinitionManager _permissionDefinitionManager;
    private readonly IAdministratorRepository _administratorRepository;

    public DatabasePermissionChecker(
        ICurrentUser currentUser,
        IPermissionDefinitionManager permissionDefinitionManager,
        IAdministratorRepository administratorRepository)
    {
        _currentUser = currentUser;
        _permissionDefinitionManager = permissionDefinitionManager;
        _administratorRepository = administratorRepository;
    }

    public async Task<bool> IsGrantedAsync(
        string permissionName,
        CancellationToken cancellationToken = default)
    {
        if (!_currentUser.IsAuthenticated ||
            _permissionDefinitionManager.GetOrNull(permissionName) is null ||
            !AdministratorIdentity.TryGetId(_currentUser, out var administratorId))
        {
            return false;
        }

        var snapshot = await _administratorRepository.GetAuthorizationSnapshotAsync(
            administratorId,
            cancellationToken);

        return snapshot is { IsActive: true } &&
            snapshot.PermissionNames.Contains(permissionName, StringComparer.Ordinal);
    }
}
