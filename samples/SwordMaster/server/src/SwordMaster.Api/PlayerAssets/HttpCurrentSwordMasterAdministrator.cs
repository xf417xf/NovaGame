using NovaCore.BuildingBlocks.CurrentUser;
using NovaCore.Shared.Security;
using SwordMaster.Application.PlayerAssets;

namespace SwordMaster.Api.PlayerAssets;

public sealed class HttpCurrentSwordMasterAdministrator :
    ICurrentSwordMasterAdministrator
{
    private readonly ICurrentUser _currentUser;

    public HttpCurrentSwordMasterAdministrator(ICurrentUser currentUser)
    {
        _currentUser = currentUser;
    }

    public bool IsAuthenticated => TryGetAdministratorId(out _);

    public Guid? AdministratorId =>
        TryGetAdministratorId(out var administratorId) ? administratorId : null;

    private bool TryGetAdministratorId(out Guid administratorId)
    {
        administratorId = Guid.Empty;
        if (!_currentUser.IsAuthenticated || _currentUser.Principal is null)
        {
            return false;
        }

        var authenticatedIdentities = _currentUser.Principal.Identities
            .Where(identity => identity.IsAuthenticated)
            .ToArray();
        if (authenticatedIdentities.Length != 1 ||
            string.Equals(
                authenticatedIdentities[0].AuthenticationType,
                SwordMasterPlayerAuthentication.Scheme,
                StringComparison.Ordinal))
        {
            return false;
        }

        var administratorIds = authenticatedIdentities[0]
            .FindAll(SecurityClaimNames.UserId)
            .Select(claim => claim.Value)
            .ToArray();
        return administratorIds.Length == 1 &&
            Guid.TryParseExact(administratorIds[0], "D", out administratorId) &&
            administratorId != Guid.Empty;
    }
}
