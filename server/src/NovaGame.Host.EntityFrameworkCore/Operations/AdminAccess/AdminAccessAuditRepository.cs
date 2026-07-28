using NovaGame.Domain.Operations.AdminAccess;

namespace NovaGame.Host.EntityFrameworkCore.Operations.AdminAccess;

public sealed class AdminAccessAuditRepository : IAdminAccessAuditRepository
{
    private readonly NovaGameDbContext _dbContext;

    public AdminAccessAuditRepository(NovaGameDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(
        AdminAccessAuditRecord auditRecord,
        CancellationToken cancellationToken = default)
    {
        await _dbContext.AdminAccessAuditRecords.AddAsync(auditRecord, cancellationToken);
    }
}
