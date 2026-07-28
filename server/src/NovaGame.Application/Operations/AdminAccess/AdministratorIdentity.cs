using System.Security.Claims;
using NovaCore.BuildingBlocks.CurrentUser;

namespace NovaGame.Application.Operations.AdminAccess;

internal static class AdministratorIdentity
{
    public static bool TryGetId(ICurrentUser currentUser, out Guid administratorId)
    {
        ArgumentNullException.ThrowIfNull(currentUser);

        var value = currentUser.Id ??
            currentUser.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out administratorId);
    }
}
