using NovaGame.Domain.Content.GameConfig;

namespace NovaGame.Host.EntityFrameworkCore.Content.GameConfig;

public sealed class GameConfigAuditRepository : IGameConfigAuditRepository
{
    private readonly NovaGameDbContext _dbContext;

    public GameConfigAuditRepository(NovaGameDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(
        GameConfigAuditRecord auditRecord,
        CancellationToken cancellationToken = default)
    {
        await _dbContext.GameConfigAuditRecords.AddAsync(auditRecord, cancellationToken);
    }
}
