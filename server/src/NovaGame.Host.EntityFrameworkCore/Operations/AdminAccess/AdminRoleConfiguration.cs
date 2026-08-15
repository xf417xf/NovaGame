using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NovaGame.Domain.Operations.AdminAccess;

namespace NovaGame.Host.EntityFrameworkCore.Operations.AdminAccess;

public sealed class AdminRoleConfiguration : IEntityTypeConfiguration<AdminRole>
{
    public void Configure(EntityTypeBuilder<AdminRole> builder)
    {
        builder.ToTable("admin_roles", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint(
                "ck_admin_roles_revision",
                "`Revision` > 0");
            tableBuilder.HasCheckConstraint(
                "ck_admin_roles_built_in_identity",
                "`IsBuiltIn` = 0 OR (`IsActive` = 1 AND BINARY `Name` = BINARY 'Administrators' AND BINARY `NormalizedName` = BINARY 'ADMINISTRATORS')");
        });
        builder.HasKey(role => role.Id);

        builder.Property(role => role.Id).HasColumnType("char(36)");
        builder.Property(role => role.Name)
            .HasMaxLength(64)
            .UseCollation("utf8mb4_bin")
            .IsRequired();
        builder.Property(role => role.NormalizedName)
            .HasMaxLength(64)
            .UseCollation("utf8mb4_bin")
            .IsRequired();
        builder.Property(role => role.CreatedAtUtc).HasPrecision(6);
        builder.Property(role => role.IsActive)
            .HasField("_managementIsActive")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnType("tinyint(1)")
            .HasDefaultValue(true);
        builder.Property(role => role.UpdatedAtUtc)
            .HasField("_managementUpdatedAtUtc")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasPrecision(6);
        builder.Property(role => role.Revision)
            .HasField("_managementRevision")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnType("bigint")
            .HasDefaultValue(1L)
            .IsConcurrencyToken();
        builder.Property<byte?>("BuiltInRoleSlot")
            .HasColumnType("tinyint")
            .HasComputedColumnSql("CASE WHEN `IsBuiltIn` = 1 THEN 1 ELSE NULL END", stored: true);

        builder.HasIndex(role => role.NormalizedName)
            .IsUnique()
            .HasDatabaseName("ux_admin_roles_normalized_name");
        builder.HasIndex("BuiltInRoleSlot")
            .IsUnique()
            .HasDatabaseName("ux_admin_roles_built_in_slot");
        builder.HasIndex(role => new { role.IsBuiltIn, role.NormalizedName, role.Id })
            .IsDescending(true, false, false)
            .HasDatabaseName("ix_admin_roles_list");
        builder.HasIndex(role => new { role.IsActive, role.IsBuiltIn, role.NormalizedName, role.Id })
            .IsDescending(false, true, false, false)
            .HasDatabaseName("ix_admin_roles_active_list");

        builder.HasMany(role => role.Permissions)
            .WithOne()
            .HasForeignKey(permission => permission.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(role => role.Permissions)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
