using Microsoft.EntityFrameworkCore;
using SwordMaster.Domain.PlayerAssets;

namespace SwordMaster.Host.EntityFrameworkCore.PlayerAssets;

public sealed class PlayerEquipmentGrantRepository : IPlayerEquipmentGrantRepository
{
    private const string PlayerIdPropertyName = "PlayerIdValue";
    private readonly SwordMasterDbContext _dbContext;

    public PlayerEquipmentGrantRepository(SwordMasterDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<long> GetCountAsync(
        PlayerId playerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        return QueryForPlayer(playerId)
            .AsNoTracking()
            .LongCountAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PlayerEquipmentGrant>> GetListAsync(
        PlayerId playerId,
        int skipCount,
        int maxResultCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        ArgumentOutOfRangeException.ThrowIfNegative(skipCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxResultCount, 1);

        return await QueryForPlayer(playerId)
            .AsNoTracking()
            .OrderByDescending(grant => grant.GrantedAtUtc)
            .ThenByDescending(grant => grant.Id)
            .Skip(skipCount)
            .Take(maxResultCount)
            .ToListAsync(cancellationToken);
    }

    public Task<PlayerEquipmentGrant?> FindByGrantIdempotencyKeyAsync(
        PlayerId playerId,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ValidateLookup(playerId, idempotencyKey, nameof(idempotencyKey));
        return QueryForPlayer(playerId)
            .AsNoTracking()
            .SingleOrDefaultAsync(
                grant => grant.GrantIdempotencyKey == idempotencyKey,
                cancellationToken);
    }

    public Task<PlayerEquipmentGrant?> FindByRollbackIdempotencyKeyAsync(
        PlayerId playerId,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ValidateLookup(playerId, idempotencyKey, nameof(idempotencyKey));
        return QueryForPlayer(playerId)
            .AsNoTracking()
            .SingleOrDefaultAsync(
                grant => grant.RollbackIdempotencyKey == idempotencyKey,
                cancellationToken);
    }

    public async Task<PlayerEquipmentGrant?> FindByIdForUpdateAsync(
        PlayerId playerId,
        Guid grantId,
        CancellationToken cancellationToken = default)
    {
        ValidateLookup(playerId, grantId, nameof(grantId));
        EnsureActiveTransaction();

        var playerIdText = playerId.Value.ToString("D");
        var grantIdText = grantId.ToString("D");
        var grants = await _dbContext.PlayerEquipmentGrants
            .FromSqlInterpolated($"""
                SELECT *
                FROM `sm_player_equipment_grants`
                WHERE `PlayerId` = {playerIdText}
                  AND `GrantId` = {grantIdText}
                LIMIT 1 FOR UPDATE
                """)
            .ToListAsync(cancellationToken);
        return grants.SingleOrDefault();
    }

    public async Task AddAsync(
        PlayerEquipmentGrant grant,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grant);
        await _dbContext.PlayerEquipmentGrants.AddAsync(grant, cancellationToken);
    }

    private IQueryable<PlayerEquipmentGrant> QueryForPlayer(PlayerId playerId)
    {
        var playerIdValue = playerId.Value;
        return _dbContext.PlayerEquipmentGrants.Where(
            grant => EF.Property<Guid>(grant, PlayerIdPropertyName) == playerIdValue);
    }

    private static void ValidateLookup(PlayerId playerId, Guid value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifiers cannot be empty.", parameterName);
        }
    }

    private void EnsureActiveTransaction()
    {
        if (_dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "A transactional unit of work is required before locking an equipment grant.");
        }
    }
}
