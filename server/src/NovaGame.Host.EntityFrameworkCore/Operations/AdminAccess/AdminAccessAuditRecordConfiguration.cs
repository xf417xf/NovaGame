using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NovaGame.Domain.Operations.AdminAccess;

namespace NovaGame.Host.EntityFrameworkCore.Operations.AdminAccess;

public sealed class AdminAccessAuditRecordConfiguration : IEntityTypeConfiguration<AdminAccessAuditRecord>
{
    public void Configure(EntityTypeBuilder<AdminAccessAuditRecord> builder)
    {
        builder.ToTable("admin_audit_records");
        builder.HasKey(auditRecord => auditRecord.Id);

        builder.Property(auditRecord => auditRecord.Id).HasColumnType("char(36)");
        builder.Property(auditRecord => auditRecord.Action)
            .HasConversion<string>()
            .HasMaxLength(48)
            .IsRequired();
        builder.Property(auditRecord => auditRecord.OccurredAtUtc).HasPrecision(6);
        builder.Property(auditRecord => auditRecord.AdministratorId).HasColumnType("char(36)");
        builder.Property(auditRecord => auditRecord.ActorAdministratorId).HasColumnType("char(36)");
        builder.Property(auditRecord => auditRecord.RoleId).HasColumnType("char(36)");
        builder.Property(auditRecord => auditRecord.SubjectUserName).HasMaxLength(64);
        builder.Property(auditRecord => auditRecord.PermissionName).HasMaxLength(128);
        builder.Property(auditRecord => auditRecord.ErrorCode).HasMaxLength(128);
        builder.Property(auditRecord => auditRecord.IpAddress).HasMaxLength(64);
        builder.Property(auditRecord => auditRecord.UserAgent).HasMaxLength(512);
        builder.Property(auditRecord => auditRecord.CorrelationId).HasMaxLength(128);

        builder.HasIndex(auditRecord => new { auditRecord.AdministratorId, auditRecord.OccurredAtUtc })
            .HasDatabaseName("ix_admin_audit_records_admin_time");
        builder.HasIndex(auditRecord => new { auditRecord.Action, auditRecord.OccurredAtUtc })
            .HasDatabaseName("ix_admin_audit_records_action_time");
    }
}
