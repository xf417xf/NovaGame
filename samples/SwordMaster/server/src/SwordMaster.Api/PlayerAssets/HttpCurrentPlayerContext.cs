using Microsoft.AspNetCore.Http;
using NovaCore.Shared.Security;
using SwordMaster.Application.PlayerAssets;

namespace SwordMaster.Api.PlayerAssets;

public sealed class HttpCurrentPlayerContext : ICurrentPlayerContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpCurrentPlayerContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public bool IsAuthenticated => TryGetPlayerId(out _);

    public Guid? PlayerId => TryGetPlayerId(out var playerId) ? playerId : null;

    private bool TryGetPlayerId(out Guid playerId)
    {
        playerId = Guid.Empty;
        var principal = _httpContextAccessor.HttpContext?.User;
        var identities = principal?
            .Identities
            .Where(identity =>
                identity.IsAuthenticated &&
                string.Equals(
                    identity.AuthenticationType,
                    SwordMasterPlayerAuthentication.Scheme,
                    StringComparison.Ordinal))
            .ToArray() ?? [];
        if (identities.Length != 1)
        {
            return false;
        }

        var playerIds = identities[0]
            .FindAll(SecurityClaimNames.UserId)
            .Select(claim => claim.Value)
            .ToArray();
        var tokenUses = identities[0]
            .FindAll(SwordMasterPlayerAuthentication.TokenUseClaim)
            .Select(claim => claim.Value)
            .ToArray();

        return playerIds.Length == 1 &&
            Guid.TryParse(playerIds[0], out playerId) &&
            playerId != Guid.Empty &&
            tokenUses.Length == 1 &&
            string.Equals(
                tokenUses[0],
                SwordMasterPlayerAuthentication.PlayerTokenUse,
                StringComparison.Ordinal);
    }
}
