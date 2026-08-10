using Microsoft.EntityFrameworkCore;
using SwordMaster.Domain.PlayerAssets;

namespace SwordMaster.Host.EntityFrameworkCore.PlayerAssets;

public sealed class PlayerAssetCommandReceiptRepository :
    IPlayerAssetCommandReceiptRepository
{
    private readonly SwordMasterDbContext _dbContext;

    public PlayerAssetCommandReceiptRepository(SwordMasterDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<PlayerAssetCommandReceipt?> FindAsync(
        PlayerId playerId,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        return FindCoreAsync(
            playerId,
            idempotencyKey,
            forUpdate: false,
            cancellationToken);
    }

    public Task<PlayerAssetCommandReceipt?> FindForUpdateAsync(
        PlayerId playerId,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        EnsureActiveTransaction();
        return FindCoreAsync(
            playerId,
            idempotencyKey,
            forUpdate: true,
            cancellationToken);
    }

    public async Task AddAsync(
        PlayerAssetCommandReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        await _dbContext.PlayerAssetCommandReceipts.AddAsync(receipt, cancellationToken);
    }

    private async Task<PlayerAssetCommandReceipt?> FindCoreAsync(
        PlayerId playerId,
        Guid idempotencyKey,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        if (idempotencyKey == Guid.Empty)
        {
            throw new ArgumentException(
                "Idempotency keys cannot be empty.",
                nameof(idempotencyKey));
        }

        var playerIdText = playerId.Value.ToString("D");
        var idempotencyKeyText = idempotencyKey.ToString("D");
        var receipts = forUpdate
            ? await _dbContext.PlayerAssetCommandReceipts
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM `sm_player_asset_command_receipts`
                    WHERE `PlayerId` = {playerIdText}
                      AND `IdempotencyKey` = {idempotencyKeyText}
                    LIMIT 1 FOR UPDATE
                    """)
                .AsNoTracking()
                .ToListAsync(cancellationToken)
            : await _dbContext.PlayerAssetCommandReceipts
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM `sm_player_asset_command_receipts`
                    WHERE `PlayerId` = {playerIdText}
                      AND `IdempotencyKey` = {idempotencyKeyText}
                    LIMIT 1
                    """)
                .AsNoTracking()
                .ToListAsync(cancellationToken);
        return receipts.SingleOrDefault();
    }

    private void EnsureActiveTransaction()
    {
        if (_dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "A transactional unit of work is required before locking a player-assets command receipt.");
        }
    }
}
