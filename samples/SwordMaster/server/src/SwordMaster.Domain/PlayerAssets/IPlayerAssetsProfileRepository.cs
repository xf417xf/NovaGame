namespace SwordMaster.Domain.PlayerAssets;

public interface IPlayerAssetsProfileRepository
{
    Task<PlayerAssetsProfile?> FindByPlayerIdAsync(
        PlayerId playerId,
        CancellationToken cancellationToken = default);

    Task<PlayerAssetsProfile?> FindByPlayerIdForUpdateAsync(
        PlayerId playerId,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        PlayerAssetsProfile profile,
        CancellationToken cancellationToken = default);
}
