using Microsoft.EntityFrameworkCore;
using NovaGame.Domain.Operations.AdminAccess;

namespace NovaGame.Host.EntityFrameworkCore.Operations.AdminAccess;

public sealed class AdminAccessCommandReceiptRepository : IAdminAccessCommandReceiptRepository
{
    private readonly NovaGameDbContext _dbContext;

    public AdminAccessCommandReceiptRepository(NovaGameDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<AdminAccessCommandReceipt> ClaimAsync(
        AdminAccessCommandReceipt candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var receiptId = candidate.Id.ToString();
        var actorAdministratorId = candidate.ActorAdministratorId.ToString();
        var commandKind = candidate.CommandKind.ToString();
        var idempotencyKey = candidate.IdempotencyKey.ToString();
        var targetResourceId = candidate.TargetResourceId.ToString();
        await _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO `admin_access_command_receipts` (
                `Id`,
                `ActorAdministratorId`,
                `CommandKind`,
                `IdempotencyKey`,
                `RequestFingerprint`,
                `TargetResourceId`,
                `ResultingRevision`,
                `CreatedAtUtc`)
            VALUES (
                {receiptId},
                {actorAdministratorId},
                {commandKind},
                {idempotencyKey},
                {candidate.RequestFingerprint},
                {targetResourceId},
                {candidate.ResultingRevision},
                {candidate.CreatedAtUtc})
            ON DUPLICATE KEY UPDATE `Id` = `Id`
            """, cancellationToken);

        var receipts = await _dbContext.AdminAccessCommandReceipts
            .FromSqlInterpolated($"""
                SELECT *
                FROM `admin_access_command_receipts`
                WHERE `ActorAdministratorId` = {actorAdministratorId}
                    AND `CommandKind` = {commandKind}
                    AND `IdempotencyKey` = {idempotencyKey}
                ORDER BY `ActorAdministratorId`, `CommandKind`, `IdempotencyKey`, `Id`
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);
        return receipts.Single();
    }
}
