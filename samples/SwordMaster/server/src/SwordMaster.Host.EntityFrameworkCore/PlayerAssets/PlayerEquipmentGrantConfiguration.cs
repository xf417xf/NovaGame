using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SwordMaster.Domain.PlayerAssets;

namespace SwordMaster.Host.EntityFrameworkCore.PlayerAssets;

public sealed class PlayerEquipmentGrantConfiguration :
    IEntityTypeConfiguration<PlayerEquipmentGrant>
{
    private const string OrdinalCollation = "utf8mb4_0900_bin";
    private const string AsciiOrdinalCollation = "ascii_bin";

    public void Configure(EntityTypeBuilder<PlayerEquipmentGrant> builder)
    {
        builder.ToTable(
            "sm_player_equipment_grants",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint(
                    "ck_sm_player_equipment_grants_status",
                    "`Status` IN (1, 2)");
                tableBuilder.HasCheckConstraint(
                    "ck_sm_player_equipment_grants_grant_revision",
                    "`GrantResultingRevision` > 0");
                tableBuilder.HasCheckConstraint(
                    "ck_sm_player_equipment_grants_rollback_revision",
                    "`RollbackResultingRevision` IS NULL OR `RollbackResultingRevision` > 0");
                tableBuilder.HasCheckConstraint(
                    "ck_sm_player_equipment_grants_rollback_complete",
                    "((`Status` = 1 AND `RolledBackByAdministratorId` IS NULL " +
                    "AND `RolledBackAtUtc` IS NULL AND `RollbackReason` IS NULL " +
                    "AND `RollbackIdempotencyKey` IS NULL " +
                    "AND `RollbackRequestFingerprint` IS NULL " +
                    "AND `RollbackResultingRevision` IS NULL " +
                    "AND `RollbackIpAddress` IS NULL AND `RollbackUserAgent` IS NULL " +
                    "AND `RollbackCorrelationId` IS NULL) OR " +
                    "(`Status` = 2 AND `RolledBackByAdministratorId` IS NOT NULL " +
                    "AND `RolledBackAtUtc` IS NOT NULL AND `RollbackReason` IS NOT NULL " +
                    "AND `RollbackIdempotencyKey` IS NOT NULL " +
                    "AND `RollbackRequestFingerprint` IS NOT NULL " +
                    "AND `RollbackResultingRevision` IS NOT NULL))");
                tableBuilder.HasCheckConstraint(
                    "ck_sm_player_equipment_grants_rollback_time",
                    "`RolledBackAtUtc` IS NULL OR `RolledBackAtUtc` >= `GrantedAtUtc`");
            });

        builder.HasKey(grant => grant.Id);
        builder.Property(grant => grant.Id)
            .HasColumnName("GrantId")
            .HasColumnType("char(36)")
            .ValueGeneratedNever();
        builder.Ignore(grant => grant.GrantId);
        builder.Property<Guid>("PlayerIdValue")
            .HasColumnName("PlayerId")
            .HasColumnType("char(36)")
            .ValueGeneratedNever();
        builder.Ignore(grant => grant.PlayerId);
        builder.Property(grant => grant.OwnedItemId)
            .HasConversion<OwnedItemIdValueConverter>()
            .HasColumnType("char(36)")
            .ValueGeneratedNever();
        builder.Property(grant => grant.ConfiguredItemId)
            .HasMaxLength(EquipmentDefinition.ConfiguredItemIdMaximumLength)
            .UseCollation(OrdinalCollation)
            .IsRequired();
        builder.Property(grant => grant.ConfigurationVersionId)
            .HasColumnType("char(36)")
            .ValueGeneratedNever();
        builder.Property(grant => grant.Status)
            .HasConversion<byte>()
            .HasColumnType("tinyint unsigned")
            .ValueGeneratedNever();

        ConfigureGrantEvidence(builder);
        ConfigureRollbackEvidence(builder);
        ConfigureIndexesAndRelationship(builder);
    }

    private static void ConfigureGrantEvidence(
        EntityTypeBuilder<PlayerEquipmentGrant> builder)
    {
        builder.Property(grant => grant.GrantedByAdministratorId)
            .HasColumnType("char(36)")
            .ValueGeneratedNever();
        builder.Property(grant => grant.GrantedAtUtc).HasPrecision(6);
        builder.Property(grant => grant.GrantReason)
            .HasMaxLength(PlayerEquipmentGrant.ReasonMaximumLength)
            .IsRequired();
        builder.Property(grant => grant.GrantIdempotencyKey)
            .HasColumnType("char(36)")
            .ValueGeneratedNever();
        builder.Property(grant => grant.GrantRequestFingerprint)
            .HasColumnType("char(64)")
            .HasCharSet("ascii")
            .UseCollation(AsciiOrdinalCollation)
            .IsRequired();
        builder.Property(grant => grant.GrantResultingRevision)
            .ValueGeneratedNever();
        builder.Property(grant => grant.GrantIpAddress)
            .HasMaxLength(PlayerEquipmentGrant.IpAddressMaximumLength);
        builder.Property(grant => grant.GrantUserAgent)
            .HasMaxLength(PlayerEquipmentGrant.UserAgentMaximumLength);
        builder.Property(grant => grant.GrantCorrelationId)
            .HasMaxLength(PlayerEquipmentGrant.CorrelationIdMaximumLength);
    }

    private static void ConfigureRollbackEvidence(
        EntityTypeBuilder<PlayerEquipmentGrant> builder)
    {
        builder.Property(grant => grant.RolledBackByAdministratorId)
            .HasColumnType("char(36)")
            .ValueGeneratedNever();
        builder.Property(grant => grant.RolledBackAtUtc).HasPrecision(6);
        builder.Property(grant => grant.RollbackReason)
            .HasMaxLength(PlayerEquipmentGrant.ReasonMaximumLength);
        builder.Property(grant => grant.RollbackIdempotencyKey)
            .HasColumnType("char(36)")
            .ValueGeneratedNever();
        builder.Property(grant => grant.RollbackRequestFingerprint)
            .HasColumnType("char(64)")
            .HasCharSet("ascii")
            .UseCollation(AsciiOrdinalCollation);
        builder.Property(grant => grant.RollbackResultingRevision)
            .ValueGeneratedNever();
        builder.Property(grant => grant.RollbackIpAddress)
            .HasMaxLength(PlayerEquipmentGrant.IpAddressMaximumLength);
        builder.Property(grant => grant.RollbackUserAgent)
            .HasMaxLength(PlayerEquipmentGrant.UserAgentMaximumLength);
        builder.Property(grant => grant.RollbackCorrelationId)
            .HasMaxLength(PlayerEquipmentGrant.CorrelationIdMaximumLength);
    }

    private static void ConfigureIndexesAndRelationship(
        EntityTypeBuilder<PlayerEquipmentGrant> builder)
    {
        builder.HasIndex("PlayerIdValue", nameof(PlayerEquipmentGrant.OwnedItemId))
            .IsUnique()
            .HasDatabaseName("ux_sm_player_equipment_grants_player_item");
        builder.HasIndex("PlayerIdValue", nameof(PlayerEquipmentGrant.GrantIdempotencyKey))
            .IsUnique()
            .HasDatabaseName("ux_sm_player_equipment_grants_player_grant_key");
        builder.HasIndex("PlayerIdValue", nameof(PlayerEquipmentGrant.RollbackIdempotencyKey))
            .IsUnique()
            .HasDatabaseName("ux_sm_player_equipment_grants_player_rollback_key");
        builder.HasIndex(
                "PlayerIdValue",
                nameof(PlayerEquipmentGrant.GrantedAtUtc),
                nameof(PlayerEquipmentGrant.Id))
            .HasDatabaseName("ix_sm_player_equipment_grants_player_granted");

        builder.HasOne<PlayerAssetsProfile>()
            .WithMany()
            .HasForeignKey("PlayerIdValue")
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_sm_player_equipment_grants_player");
    }
}
