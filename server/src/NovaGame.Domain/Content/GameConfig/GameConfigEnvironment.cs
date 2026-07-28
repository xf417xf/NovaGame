using NovaCore.BuildingBlocks.Domain;
using NovaGame.Domain.Shared.Content.GameConfig;

namespace NovaGame.Domain.Content.GameConfig;

public sealed class GameConfigEnvironment : AggregateRoot<Guid>
{
    private GameConfigEnvironment()
    {
    }

    public GameConfigEnvironment(
        Guid id,
        GameConfigSetKey configurationSetKey,
        GameConfigEnvironmentName environmentName,
        Guid activeVersionId,
        Guid activatedByAdministratorId,
        DateTime activatedAtUtc,
        string? reason = null)
    {
        if (id == Guid.Empty || activeVersionId == Guid.Empty || activatedByAdministratorId == Guid.Empty)
        {
            throw new ArgumentException("Environment, version, and administrator identifiers cannot be empty.");
        }

        Id = id;
        ConfigurationSetKey = configurationSetKey?.Value ??
            throw new ArgumentNullException(nameof(configurationSetKey));
        EnvironmentName = environmentName?.Value ??
            throw new ArgumentNullException(nameof(environmentName));
        ActiveVersionId = activeVersionId;
        PreviousVersionId = null;
        Revision = 1;
        ActivatedByAdministratorId = activatedByAdministratorId;
        ActivatedAtUtc = RequireUtc(activatedAtUtc, nameof(activatedAtUtc));
        Reason = NormalizeReason(reason);
    }

    public string ConfigurationSetKey { get; private set; } = string.Empty;

    public string EnvironmentName { get; private set; } = string.Empty;

    public Guid ActiveVersionId { get; private set; }

    public Guid? PreviousVersionId { get; private set; }

    public long Revision { get; private set; }

    public Guid ActivatedByAdministratorId { get; private set; }

    public DateTime ActivatedAtUtc { get; private set; }

    public string? Reason { get; private set; }

    public GameConfigEnvironmentTransition Activate(
        Guid versionId,
        long expectedRevision,
        Guid activatedByAdministratorId,
        DateTime activatedAtUtc,
        string? reason = null)
    {
        if (versionId == Guid.Empty || activatedByAdministratorId == Guid.Empty)
        {
            throw new ArgumentException("Version and administrator identifiers cannot be empty.");
        }

        var requiredActivatedAtUtc = RequireUtc(activatedAtUtc, nameof(activatedAtUtc));
        if (versionId == ActiveVersionId)
        {
            return new GameConfigEnvironmentTransition(
                PreviousVersionId,
                ActiveVersionId,
                Revision,
                Changed: false,
                Reason);
        }

        if (expectedRevision != Revision)
        {
            throw new GameConfigDomainException(
                GameConfigErrorCodes.ActivationConflict,
                $"Environment revision {expectedRevision} is stale; the current revision is {Revision}.");
        }

        var previousVersionId = ActiveVersionId;
        PreviousVersionId = previousVersionId;
        ActiveVersionId = versionId;
        Revision = checked(Revision + 1);
        ActivatedByAdministratorId = activatedByAdministratorId;
        ActivatedAtUtc = requiredActivatedAtUtc;
        Reason = NormalizeReason(reason);
        return new GameConfigEnvironmentTransition(
            previousVersionId,
            versionId,
            Revision,
            Changed: true,
            Reason);
    }

    private static DateTime RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The timestamp must use UTC.", parameterName);
        }

        return value;
    }

    private static string? NormalizeReason(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (normalized.Length > GameConfigConstants.RollbackReasonMaximumLength)
        {
            throw new ArgumentException(
                $"Activation reasons cannot exceed {GameConfigConstants.RollbackReasonMaximumLength} characters.",
                nameof(value));
        }

        return normalized;
    }
}

public sealed record GameConfigEnvironmentTransition(
    Guid? PreviousVersionId,
    Guid ActiveVersionId,
    long Revision,
    bool Changed,
    string? Reason);
