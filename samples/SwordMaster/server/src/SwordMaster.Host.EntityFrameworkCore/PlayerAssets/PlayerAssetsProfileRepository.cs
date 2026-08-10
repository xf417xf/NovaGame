using Microsoft.EntityFrameworkCore;
using SwordMaster.Domain.PlayerAssets;

namespace SwordMaster.Host.EntityFrameworkCore.PlayerAssets;

public sealed class PlayerAssetsProfileRepository : IPlayerAssetsProfileRepository
{
    private readonly SwordMasterDbContext _dbContext;

    public PlayerAssetsProfileRepository(SwordMasterDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<PlayerAssetsProfile?> FindByPlayerIdAsync(
        PlayerId playerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        return IncludeAggregate(_dbContext.PlayerAssetsProfiles)
            .AsNoTracking()
            .SingleOrDefaultAsync(
                profile => profile.Id == playerId.Value,
                cancellationToken);
    }

    public async Task<PlayerAssetsProfile?> FindByPlayerIdForUpdateAsync(
        PlayerId playerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        EnsureActiveTransaction();

        var playerIdText = playerId.Value.ToString("D");
        var profiles = await IncludeAggregate(
                _dbContext.PlayerAssetsProfiles.FromSqlInterpolated($"""
                    SELECT *
                    FROM `sm_player_assets_profiles`
                    WHERE `PlayerId` = {playerIdText}
                    LIMIT 1 FOR UPDATE
                    """))
            .ToListAsync(cancellationToken);
        return profiles.SingleOrDefault();
    }

    public async Task AddAsync(
        PlayerAssetsProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        await _dbContext.PlayerAssetsProfiles.AddAsync(profile, cancellationToken);
    }

    private static IQueryable<PlayerAssetsProfile> IncludeAggregate(
        IQueryable<PlayerAssetsProfile> query)
    {
        return query
            .Include(profile => profile.Character)
                .ThenInclude(character => character.BaseAttributes)
            .Include(profile => profile.OwnedItems)
            .Include(profile => profile.EquipmentAssignments)
            .AsSplitQuery();
    }

    private void EnsureActiveTransaction()
    {
        if (_dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "A transactional unit of work is required before locking a player-assets profile.");
        }
    }
}
