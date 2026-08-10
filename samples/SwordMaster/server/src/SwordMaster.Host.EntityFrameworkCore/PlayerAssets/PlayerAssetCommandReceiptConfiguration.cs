using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SwordMaster.Domain.PlayerAssets;

namespace SwordMaster.Host.EntityFrameworkCore.PlayerAssets;

public sealed class PlayerAssetCommandReceiptConfiguration :
    IEntityTypeConfiguration<PlayerAssetCommandReceipt>
{
    private const string OrdinalCollation = "utf8mb4_0900_bin";

    public void Configure(EntityTypeBuilder<PlayerAssetCommandReceipt> builder)
    {
        builder.ToTable("sm_player_asset_command_receipts");
        builder.HasKey(receipt => new { receipt.PlayerId, receipt.IdempotencyKey });
        builder.Property(receipt => receipt.PlayerId)
            .HasConversion<PlayerIdValueConverter>()
            .HasColumnType("char(36)")
            .ValueGeneratedNever();
        builder.Property(receipt => receipt.IdempotencyKey)
            .HasColumnType("char(36)")
            .ValueGeneratedNever();
        builder.Property(receipt => receipt.RequestFingerprint)
            .HasMaxLength(PlayerAssetCommandReceipt.RequestFingerprintMaximumLength)
            .UseCollation(OrdinalCollation)
            .IsRequired();
        builder.Property(receipt => receipt.ResultingRevision).ValueGeneratedNever();
        builder.Property(receipt => receipt.CompletedAtUtc).HasPrecision(6);
    }
}
