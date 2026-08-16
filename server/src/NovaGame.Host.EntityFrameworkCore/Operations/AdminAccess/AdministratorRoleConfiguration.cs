using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NovaGame.Domain.Operations.AdminAccess;

namespace NovaGame.Host.EntityFrameworkCore.Operations.AdminAccess;

public sealed class AdministratorRoleConfiguration : IEntityTypeConfiguration<AdministratorRole>
{
    public void Configure(EntityTypeBuilder<AdministratorRole> builder)
    {
        builder.ToTable("admin_role_memberships");
        builder.HasKey(membership => membership.Id);

        builder.Property(membership => membership.Id)
            .HasColumnType("char(36)")
            .ValueGeneratedNever();
        builder.Property(membership => membership.AdministratorId).HasColumnType("char(36)");
        builder.Property(membership => membership.RoleId).HasColumnType("char(36)");
        builder.Property(membership => membership.AssignedByAdministratorId).HasColumnType("char(36)");
        builder.Property(membership => membership.AssignedAtUtc).HasPrecision(6);

        builder.HasIndex(membership => new { membership.AdministratorId, membership.RoleId })
            .IsUnique()
            .HasDatabaseName("ux_admin_role_memberships_admin_role");
        builder.HasIndex(membership => new { membership.RoleId, membership.AdministratorId })
            .HasDatabaseName("ix_admin_role_memberships_role_admin");

        builder.HasOne<AdminRole>()
            .WithMany()
            .HasForeignKey(membership => membership.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
