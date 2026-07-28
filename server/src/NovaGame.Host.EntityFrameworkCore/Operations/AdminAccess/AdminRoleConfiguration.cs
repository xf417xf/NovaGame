using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NovaGame.Domain.Operations.AdminAccess;

namespace NovaGame.Host.EntityFrameworkCore.Operations.AdminAccess;

public sealed class AdminRoleConfiguration : IEntityTypeConfiguration<AdminRole>
{
    public void Configure(EntityTypeBuilder<AdminRole> builder)
    {
        builder.ToTable("admin_roles");
        builder.HasKey(role => role.Id);

        builder.Property(role => role.Id).HasColumnType("char(36)");
        builder.Property(role => role.Name).HasMaxLength(64).IsRequired();
        builder.Property(role => role.NormalizedName).HasMaxLength(64).IsRequired();
        builder.Property(role => role.CreatedAtUtc).HasPrecision(6);

        builder.HasIndex(role => role.NormalizedName)
            .IsUnique()
            .HasDatabaseName("ux_admin_roles_normalized_name");

        builder.HasMany(role => role.Permissions)
            .WithOne()
            .HasForeignKey(permission => permission.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(role => role.Permissions)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
