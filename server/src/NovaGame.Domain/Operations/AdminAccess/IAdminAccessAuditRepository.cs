namespace NovaGame.Domain.Operations.AdminAccess;

public interface IAdminAccessAuditRepository
{
    Task AddAsync(AdminAccessAuditRecord auditRecord, CancellationToken cancellationToken = default);
}
