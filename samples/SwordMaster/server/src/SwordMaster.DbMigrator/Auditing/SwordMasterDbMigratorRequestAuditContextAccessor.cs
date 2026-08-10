using NovaGame.Application.Auditing;

namespace SwordMaster.DbMigrator.Auditing;

public sealed class SwordMasterDbMigratorRequestAuditContextAccessor : IRequestAuditContextAccessor
{
    public RequestAuditContext Current { get; } = new(null, null, null);
}
