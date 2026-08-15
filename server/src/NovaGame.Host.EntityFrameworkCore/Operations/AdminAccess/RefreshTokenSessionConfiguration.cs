using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NovaGame.Domain.Operations.AdminAccess;

namespace NovaGame.Host.EntityFrameworkCore.Operations.AdminAccess;

public sealed class RefreshTokenSessionConfiguration : IEntityTypeConfiguration<RefreshTokenSession>
{
    public void Configure(EntityTypeBuilder<RefreshTokenSession> builder)
    {
        builder.ToTable("admin_refresh_token_sessions", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint(
                "ck_admin_refresh_sessions_revocation",
                "(`RevokedAtUtc` IS NULL AND `RevocationReason` IS NULL AND `ReplacedBySessionId` IS NULL) OR (`RevokedAtUtc` IS NOT NULL AND `RevocationReason` IS NOT NULL AND ((`RevocationReason` = 'Rotated' AND `ReplacedBySessionId` IS NOT NULL) OR (`RevocationReason` <> 'Rotated' AND `ReplacedBySessionId` IS NULL)))");
        });
        builder.HasKey(session => session.Id);

        builder.Property(session => session.Id).HasColumnType("char(36)");
        builder.Property(session => session.AdministratorId).HasColumnType("char(36)");
        builder.Property(session => session.FamilyId).HasColumnType("char(36)");
        builder.Property(session => session.TokenHash).HasColumnType("binary(32)").IsRequired();
        builder.Property(session => session.CreatedAtUtc).HasPrecision(6);
        builder.Property(session => session.ExpiresAtUtc).HasPrecision(6);
        builder.Property(session => session.CreatedFromIpAddress).HasMaxLength(64);
        builder.Property(session => session.RevokedAtUtc).HasPrecision(6);
        builder.Property(session => session.RevocationReason)
            .HasConversion<string>()
            .HasMaxLength(32);
        builder.Property(session => session.RevokedFromIpAddress).HasMaxLength(64);
        builder.Property(session => session.ReplacedBySessionId).HasColumnType("char(36)");

        builder.HasIndex(session => session.TokenHash)
            .IsUnique()
            .HasDatabaseName("ux_admin_refresh_sessions_token_hash");
        builder.HasIndex(session => new { session.FamilyId, session.CreatedAtUtc, session.Id })
            .HasDatabaseName("ix_admin_refresh_sessions_family_order");
        builder.HasIndex(session => new { session.AdministratorId, session.ExpiresAtUtc })
            .HasDatabaseName("ix_admin_refresh_sessions_admin_expiry");
        builder.HasIndex(session => new
        {
            session.AdministratorId,
            session.FamilyId,
            session.CreatedAtUtc,
            session.Id
        })
            .HasDatabaseName("ix_admin_refresh_sessions_admin_family_order");
        builder.HasIndex(session => new { session.FamilyId, session.ReplacedBySessionId })
            .HasDatabaseName("ix_admin_refresh_sessions_family_replacement");

        builder.HasOne<Administrator>()
            .WithMany()
            .HasForeignKey(session => session.AdministratorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
