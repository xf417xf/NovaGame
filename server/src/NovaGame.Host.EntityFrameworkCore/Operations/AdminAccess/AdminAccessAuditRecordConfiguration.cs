using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NovaGame.Domain.Operations.AdminAccess;

namespace NovaGame.Host.EntityFrameworkCore.Operations.AdminAccess;

public sealed class AdminAccessAuditRecordConfiguration : IEntityTypeConfiguration<AdminAccessAuditRecord>
{
    public void Configure(EntityTypeBuilder<AdminAccessAuditRecord> builder)
    {
        builder.ToTable("admin_audit_records", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint(
                "ck_admin_audit_records_target_revision",
                "`TargetRevision` IS NULL OR `TargetRevision` > 0");
        });
        builder.HasKey(auditRecord => auditRecord.Id);

        builder.Property(auditRecord => auditRecord.Id).HasColumnType("char(36)");
        builder.Property(auditRecord => auditRecord.Action)
            .HasConversion<string>()
            .HasMaxLength(48)
            .UseCollation("utf8mb4_bin")
            .IsRequired();
        builder.Property(auditRecord => auditRecord.OccurredAtUtc).HasPrecision(6);
        builder.Property(auditRecord => auditRecord.AdministratorId).HasColumnType("char(36)");
        builder.Property(auditRecord => auditRecord.ActorAdministratorId).HasColumnType("char(36)");
        builder.Property(auditRecord => auditRecord.RoleId).HasColumnType("char(36)");
        builder.Property(auditRecord => auditRecord.SubjectUserName).HasMaxLength(64);
        builder.Property(auditRecord => auditRecord.PermissionName)
            .HasMaxLength(128)
            .UseCollation("utf8mb4_bin");
        builder.Property(auditRecord => auditRecord.ErrorCode).HasMaxLength(128);
        builder.Property(auditRecord => auditRecord.IpAddress).HasMaxLength(64);
        builder.Property(auditRecord => auditRecord.UserAgent).HasMaxLength(512);
        builder.Property(auditRecord => auditRecord.CorrelationId)
            .HasMaxLength(128)
            .UseCollation("utf8mb4_bin");
        builder.Property(auditRecord => auditRecord.SessionFamilyId)
            .HasField("_managementSessionFamilyId")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnType("char(36)");
        builder.Property(auditRecord => auditRecord.TargetRevision)
            .HasField("_managementTargetRevision")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnType("bigint");

        builder.HasIndex(auditRecord => new { auditRecord.OccurredAtUtc, auditRecord.Id })
            .IsDescending(true, true)
            .HasDatabaseName("ix_admin_audit_records_time_id");
        builder.HasIndex(auditRecord => new { auditRecord.ActorAdministratorId, auditRecord.OccurredAtUtc, auditRecord.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("ix_admin_audit_records_actor_time_id");
        builder.HasIndex(auditRecord => new { auditRecord.AdministratorId, auditRecord.OccurredAtUtc, auditRecord.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("ix_admin_audit_records_subject_time_id");
        builder.HasIndex(auditRecord => new { auditRecord.RoleId, auditRecord.OccurredAtUtc, auditRecord.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("ix_admin_audit_records_role_time_id");
        builder.HasIndex(auditRecord => new { auditRecord.Action, auditRecord.OccurredAtUtc, auditRecord.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("ix_admin_audit_records_action_time_id");
        builder.HasIndex(auditRecord => new { auditRecord.Succeeded, auditRecord.OccurredAtUtc, auditRecord.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("ix_admin_audit_records_success_time_id");
        builder.HasIndex(auditRecord => new { auditRecord.CorrelationId, auditRecord.OccurredAtUtc, auditRecord.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("ix_admin_audit_records_correlation_time_id");
    }
}
