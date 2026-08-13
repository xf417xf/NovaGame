namespace SwordMaster.Domain.PlayerAssets;

public interface IPlayerEquipmentGrantRepository
{
    Task<long> GetCountAsync(
        PlayerId playerId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PlayerEquipmentGrant>> GetListAsync(
        PlayerId playerId,
        int skipCount,
        int maxResultCount,
        CancellationToken cancellationToken = default);

    Task<PlayerEquipmentGrant?> FindByGrantIdempotencyKeyAsync(
        PlayerId playerId,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<PlayerEquipmentGrant?> FindByRollbackIdempotencyKeyAsync(
        PlayerId playerId,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<PlayerEquipmentGrant?> FindByIdForUpdateAsync(
        PlayerId playerId,
        Guid grantId,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        PlayerEquipmentGrant grant,
        CancellationToken cancellationToken = default);
}
