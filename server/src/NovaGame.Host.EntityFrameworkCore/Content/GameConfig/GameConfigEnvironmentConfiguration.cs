using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NovaGame.Domain.Content.GameConfig;
using NovaGame.Domain.Shared.Content.GameConfig;

namespace NovaGame.Host.EntityFrameworkCore.Content.GameConfig;

public sealed class GameConfigEnvironmentConfiguration : IEntityTypeConfiguration<GameConfigEnvironment>
{
    public void Configure(EntityTypeBuilder<GameConfigEnvironment> builder)
    {
        builder.ToTable("game_config_environments");
        builder.HasKey(environment => environment.Id);

        builder.Property(environment => environment.Id).HasColumnType("char(36)");
        builder.Property(environment => environment.ConfigurationSetKey)
            .HasMaxLength(GameConfigConstants.ConfigurationSetKeyMaximumLength)
            .IsRequired();
        builder.Property(environment => environment.EnvironmentName)
            .HasMaxLength(GameConfigConstants.EnvironmentNameMaximumLength)
            .IsRequired();
        builder.Property(environment => environment.ActiveVersionId).HasColumnType("char(36)");
        builder.Property(environment => environment.PreviousVersionId).HasColumnType("char(36)");
        builder.Property(environment => environment.Revision).IsConcurrencyToken();
        builder.Property(environment => environment.ActivatedByAdministratorId).HasColumnType("char(36)");
        builder.Property(environment => environment.ActivatedAtUtc).HasPrecision(6);
        builder.Property(environment => environment.Reason)
            .HasMaxLength(GameConfigConstants.RollbackReasonMaximumLength);

        builder.HasIndex(environment => new
        {
            environment.ConfigurationSetKey,
            environment.EnvironmentName
        })
            .IsUnique()
            .HasDatabaseName("ux_game_config_environments_set_name");
    }
}
