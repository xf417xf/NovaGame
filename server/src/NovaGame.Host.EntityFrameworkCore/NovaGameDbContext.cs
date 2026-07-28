using Microsoft.EntityFrameworkCore;
using NovaCore.BuildingBlocks.MultiTenancy;
using NovaCore.Framework.EntityFrameworkCore;
using NovaGame.Domain.Content.GameConfig;
using NovaGame.Domain.Operations.AdminAccess;

namespace NovaGame.Host.EntityFrameworkCore;

public sealed class NovaGameDbContext : BaseDbContext
{
    public NovaGameDbContext(
        DbContextOptions<NovaGameDbContext> options,
        ICurrentTenant currentTenant)
        : base(options, currentTenant)
    {
    }

    public DbSet<Administrator> Administrators => Set<Administrator>();

    public DbSet<AdminRole> AdminRoles => Set<AdminRole>();

    public DbSet<AdministratorRole> AdministratorRoles => Set<AdministratorRole>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<RefreshTokenSession> RefreshTokenSessions => Set<RefreshTokenSession>();

    public DbSet<AdminAccessAuditRecord> AdminAccessAuditRecords => Set<AdminAccessAuditRecord>();

    public DbSet<GameConfigDraft> GameConfigDrafts => Set<GameConfigDraft>();

    public DbSet<PublishedGameConfigVersion> PublishedGameConfigVersions => Set<PublishedGameConfigVersion>();

    public DbSet<GameConfigEnvironment> GameConfigEnvironments => Set<GameConfigEnvironment>();

    public DbSet<GameConfigAuditRecord> GameConfigAuditRecords => Set<GameConfigAuditRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasCharSet("utf8mb4");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NovaGameDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
