using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NovaGame.Domain.Operations.AdminAccess;

namespace NovaGame.Host.EntityFrameworkCore.Operations.AdminAccess;

public sealed class AdminAccessCommandReceiptConfiguration :
    IEntityTypeConfiguration<AdminAccessCommandReceipt>
{
    public void Configure(EntityTypeBuilder<AdminAccessCommandReceipt> builder)
    {
        builder.ToTable("admin_access_command_receipts", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint(
                "ck_admin_access_receipts_revision",
                "`ResultingRevision` > 0");
            tableBuilder.HasCheckConstraint(
                "ck_admin_access_receipts_command_kind",
                "`CommandKind` IN ('AdministratorCreate', 'AdminRoleCreate', 'AdministratorPasswordReset')");
        });
        builder.HasKey(receipt => receipt.Id);

        builder.Property(receipt => receipt.Id)
            .HasColumnType("char(36)")
            .ValueGeneratedNever();
        builder.Property(receipt => receipt.ActorAdministratorId).HasColumnType("char(36)");
        builder.Property(receipt => receipt.CommandKind)
            .HasConversion<string>()
            .HasMaxLength(48)
            .UseCollation("utf8mb4_bin")
            .IsRequired();
        builder.Property(receipt => receipt.IdempotencyKey)
            .HasColumnType("char(36)")
            .UseCollation("ascii_bin");
        builder.Property(receipt => receipt.RequestFingerprint)
            .HasField("_requestFingerprint")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnType("binary(32)")
            .IsRequired();
        builder.Property(receipt => receipt.TargetResourceId).HasColumnType("char(36)");
        builder.Property(receipt => receipt.ResultingRevision).HasColumnType("bigint");
        builder.Property(receipt => receipt.CreatedAtUtc).HasPrecision(6);

        builder.HasIndex(receipt => new
        {
            receipt.ActorAdministratorId,
            receipt.CommandKind,
            receipt.IdempotencyKey
        })
            .IsUnique()
            .HasDatabaseName("ux_admin_access_receipts_actor_kind_key");
    }
}
