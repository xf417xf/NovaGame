using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NovaGame.Domain.Content.GameConfig;
using NovaGame.Domain.Shared.Content.GameConfig;

namespace NovaGame.Host.EntityFrameworkCore.Content.GameConfig;

public sealed class GameConfigDraftConfiguration : IEntityTypeConfiguration<GameConfigDraft>
{
    public void Configure(EntityTypeBuilder<GameConfigDraft> builder)
    {
        builder.ToTable("game_config_drafts");
        builder.HasKey(draft => draft.Id);

        builder.Property(draft => draft.Id).HasColumnType("char(36)");
        builder.Property(draft => draft.ConfigurationSetKey)
            .HasMaxLength(GameConfigConstants.ConfigurationSetKeyMaximumLength)
            .IsRequired();
        builder.Property(draft => draft.Name)
            .HasMaxLength(GameConfigConstants.DraftNameMaximumLength)
            .IsRequired();
        builder.Property(draft => draft.BaseVersionId).HasColumnType("char(36)");
        builder.Property(draft => draft.SchemaVersion)
            .HasMaxLength(GameConfigConstants.SchemaVersionMaximumLength)
            .IsRequired();
        builder.Property(draft => draft.RawContentJson).HasColumnType("longtext").IsRequired();
        builder.Property(draft => draft.Revision).IsConcurrencyToken();
        builder.Property(draft => draft.CreatedByAdministratorId).HasColumnType("char(36)");
        builder.Property(draft => draft.CreatedAtUtc).HasPrecision(6);
        builder.Property(draft => draft.UpdatedByAdministratorId).HasColumnType("char(36)");
        builder.Property(draft => draft.UpdatedAtUtc).HasPrecision(6);
        builder.Property(draft => draft.ValidationStatus)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(draft => draft.ValidationProfile).HasMaxLength(128);
        builder.Property(draft => draft.LastValidatedByAdministratorId).HasColumnType("char(36)");
        builder.Property(draft => draft.LastValidatedAtUtc).HasPrecision(6);
        builder.Property(draft => draft.PublishedVersionId).HasColumnType("char(36)");

        builder.HasIndex(draft => new { draft.ConfigurationSetKey, draft.UpdatedAtUtc })
            .HasDatabaseName("ix_game_config_drafts_set_updated");
        builder.HasIndex(draft => new
        {
            draft.ConfigurationSetKey,
            draft.PublishedVersionId,
            draft.ValidationStatus
        })
            .HasDatabaseName("ix_game_config_drafts_set_state");

        builder.OwnsMany(
            draft => draft.ValidationIssues,
            issues =>
            {
                issues.ToTable("game_config_draft_validation_issues");
                issues.WithOwner().HasForeignKey("DraftId");
                issues.Property<Guid>("DraftId").HasColumnType("char(36)");
                issues.Property(issue => issue.Sequence).ValueGeneratedNever();
                issues.Property(issue => issue.Code).HasMaxLength(128).IsRequired();
                issues.Property(issue => issue.Location).HasMaxLength(1024).IsRequired();
                issues.Property(issue => issue.Message).HasMaxLength(2048).IsRequired();
                issues.HasKey("DraftId", nameof(GameConfigValidationIssue.Sequence));
            });

        builder.Navigation(draft => draft.ValidationIssues)
            .HasField("_validationIssues")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .AutoInclude(false);
    }
}
