using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NovaGame.Domain.Content.GameConfig;
using NovaGame.Domain.Shared.Content.GameConfig;

namespace NovaGame.Host.EntityFrameworkCore.Content.GameConfig;

public sealed class GameConfigAuditRecordConfiguration : IEntityTypeConfiguration<GameConfigAuditRecord>
{
    public void Configure(EntityTypeBuilder<GameConfigAuditRecord> builder)
    {
        builder.ToTable("game_config_audit_records");
        builder.HasKey(auditRecord => auditRecord.Id);

        builder.Property(auditRecord => auditRecord.Id).HasColumnType("char(36)");
        builder.Property(auditRecord => auditRecord.Action)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(auditRecord => auditRecord.ConfigurationSetKey)
            .HasMaxLength(GameConfigConstants.ConfigurationSetKeyMaximumLength)
            .IsRequired();
        builder.Property(auditRecord => auditRecord.EnvironmentName)
            .HasMaxLength(GameConfigConstants.EnvironmentNameMaximumLength);
        builder.Property(auditRecord => auditRecord.SourceDraftId).HasColumnType("char(36)");
        builder.Property(auditRecord => auditRecord.PreviousVersionId).HasColumnType("char(36)");
        builder.Property(auditRecord => auditRecord.TargetVersionId).HasColumnType("char(36)");
        builder.Property(auditRecord => auditRecord.Reason)
            .HasMaxLength(GameConfigConstants.RollbackReasonMaximumLength);
        builder.Property(auditRecord => auditRecord.ActorAdministratorId).HasColumnType("char(36)");
        builder.Property(auditRecord => auditRecord.OccurredAtUtc).HasPrecision(6);
        builder.Property(auditRecord => auditRecord.IpAddress).HasMaxLength(64);
        builder.Property(auditRecord => auditRecord.UserAgent).HasMaxLength(512);
        builder.Property(auditRecord => auditRecord.CorrelationId).HasMaxLength(128);

        builder.HasIndex(auditRecord => new
        {
            auditRecord.ConfigurationSetKey,
            auditRecord.OccurredAtUtc
        })
            .HasDatabaseName("ix_game_config_audit_set_time");
        builder.HasIndex(auditRecord => new { auditRecord.Action, auditRecord.OccurredAtUtc })
            .HasDatabaseName("ix_game_config_audit_action_time");
    }
}
