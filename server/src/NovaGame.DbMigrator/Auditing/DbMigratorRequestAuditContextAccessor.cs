using NovaGame.Application.Auditing;

namespace NovaGame.DbMigrator.Auditing;

public sealed class DbMigratorRequestAuditContextAccessor : IRequestAuditContextAccessor
{
    public RequestAuditContext Current { get; } = new(null, null, null);
}
