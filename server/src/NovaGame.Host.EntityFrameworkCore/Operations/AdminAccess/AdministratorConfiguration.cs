using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NovaGame.Domain.Operations.AdminAccess;

namespace NovaGame.Host.EntityFrameworkCore.Operations.AdminAccess;

public sealed class AdministratorConfiguration : IEntityTypeConfiguration<Administrator>
{
    public void Configure(EntityTypeBuilder<Administrator> builder)
    {
        builder.ToTable("admin_administrators", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint(
                "ck_admin_administrators_revision",
                "`Revision` > 0");
        });
        builder.HasKey(administrator => administrator.Id);

        builder.Property(administrator => administrator.Id).HasColumnType("char(36)");
        builder.Property(administrator => administrator.UserName).HasMaxLength(64).IsRequired();
        builder.Property(administrator => administrator.NormalizedUserName)
            .HasMaxLength(64)
            .UseCollation("utf8mb4_bin")
            .IsRequired();
        builder.Property(administrator => administrator.DisplayName).HasMaxLength(128).IsRequired();
        builder.Property(administrator => administrator.PasswordHash).HasMaxLength(512).IsRequired();
        builder.Property(administrator => administrator.CreatedAtUtc).HasPrecision(6);
        builder.Property(administrator => administrator.UpdatedAtUtc).HasPrecision(6);
        builder.Property(administrator => administrator.LastLoginAtUtc).HasPrecision(6);
        builder.Property(administrator => administrator.Revision)
            .HasField("_revision")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnType("bigint")
            .HasDefaultValue(1L)
            .IsConcurrencyToken();

        builder.HasIndex(administrator => administrator.NormalizedUserName)
            .IsUnique()
            .HasDatabaseName("ux_admin_administrators_normalized_name");
        builder.HasIndex(administrator => new
        {
            administrator.IsActive,
            administrator.NormalizedUserName,
            administrator.Id
        })
            .HasDatabaseName("ix_admin_administrators_active_name_id");

        builder.HasMany(administrator => administrator.RoleMemberships)
            .WithOne()
            .HasForeignKey(membership => membership.AdministratorId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(administrator => administrator.RoleMemberships)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
