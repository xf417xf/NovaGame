using Microsoft.EntityFrameworkCore;
using NovaCore.BuildingBlocks.MultiTenancy;
using NovaCore.Framework.EntityFrameworkCore;
using SwordMaster.Domain.PlayerAssets;

namespace SwordMaster.Host.EntityFrameworkCore;

public sealed class SwordMasterDbContext : BaseDbContext
{
    public SwordMasterDbContext(
        DbContextOptions<SwordMasterDbContext> options,
        ICurrentTenant currentTenant)
        : base(options, currentTenant)
    {
    }

    public DbSet<PlayerAssetsProfile> PlayerAssetsProfiles => Set<PlayerAssetsProfile>();

    public DbSet<PlayerAssetCommandReceipt> PlayerAssetCommandReceipts =>
        Set<PlayerAssetCommandReceipt>();

    public DbSet<PlayerEquipmentGrant> PlayerEquipmentGrants =>
        Set<PlayerEquipmentGrant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasCharSet("utf8mb4");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SwordMasterDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
