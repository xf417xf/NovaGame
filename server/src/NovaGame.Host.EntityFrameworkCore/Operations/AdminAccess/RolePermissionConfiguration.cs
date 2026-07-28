using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NovaGame.Domain.Operations.AdminAccess;

namespace NovaGame.Host.EntityFrameworkCore.Operations.AdminAccess;

public sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("admin_role_permissions");
        builder.HasKey(permission => permission.Id);

        builder.Property(permission => permission.Id)
            .HasColumnType("char(36)")
            .ValueGeneratedNever();
        builder.Property(permission => permission.RoleId).HasColumnType("char(36)");
        builder.Property(permission => permission.Name).HasMaxLength(128).IsRequired();
        builder.Property(permission => permission.GrantedAtUtc).HasPrecision(6);
        builder.Property(permission => permission.GrantedByAdministratorId).HasColumnType("char(36)");

        builder.HasIndex(permission => new { permission.RoleId, permission.Name })
            .IsUnique()
            .HasDatabaseName("ux_admin_role_permissions_role_name");
    }
}
