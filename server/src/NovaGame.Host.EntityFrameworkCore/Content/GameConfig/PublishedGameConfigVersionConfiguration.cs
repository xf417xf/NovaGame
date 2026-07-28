using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NovaGame.Domain.Content.GameConfig;
using NovaGame.Domain.Shared.Content.GameConfig;

namespace NovaGame.Host.EntityFrameworkCore.Content.GameConfig;

public sealed class PublishedGameConfigVersionConfiguration : IEntityTypeConfiguration<PublishedGameConfigVersion>
{
    public void Configure(EntityTypeBuilder<PublishedGameConfigVersion> builder)
    {
        builder.ToTable("game_config_published_versions");
        builder.HasKey(version => version.Id);

        builder.Property(version => version.Id).HasColumnType("char(36)");
        builder.Property(version => version.ConfigurationSetKey)
            .HasMaxLength(GameConfigConstants.ConfigurationSetKeyMaximumLength)
            .IsRequired();
        builder.Property(version => version.SchemaVersion)
            .HasMaxLength(GameConfigConstants.SchemaVersionMaximumLength)
            .IsRequired();
        builder.Property(version => version.SourceDraftId).HasColumnType("char(36)");
        builder.Property(version => version.ReleaseNotes)
            .HasMaxLength(GameConfigConstants.ReleaseNotesMaximumLength)
            .IsRequired();
        builder.Property(version => version.CanonicalContentJson).HasColumnType("longtext").IsRequired();
        builder.Property(version => version.ContentHash).HasColumnType("char(64)").IsRequired();
        builder.Property(version => version.SnapshotHash).HasColumnType("char(64)").IsRequired();
        builder.Property(version => version.SnapshotJson).HasColumnType("longtext").IsRequired();
        builder.Property(version => version.PublishedByAdministratorId).HasColumnType("char(36)");
        builder.Property(version => version.PublishedAtUtc).HasPrecision(6);

        builder.HasIndex(version => new { version.SourceDraftId, version.SourceDraftRevision })
            .IsUnique()
            .HasDatabaseName("ux_game_config_versions_source_revision");
        builder.HasIndex(version => new
        {
            version.ConfigurationSetKey,
            version.SchemaVersion,
            version.PublishedAtUtc
        })
            .HasDatabaseName("ix_game_config_versions_set_schema_time");
    }
}
