using NovaCore.BuildingBlocks.Domain;
using NovaGame.Domain.Shared.Content.GameConfig;

namespace NovaGame.Domain.Content.GameConfig;

public enum GameConfigAuditAction
{
    Published = 1,
    Activated = 2,
    RolledBack = 3
}

public sealed class GameConfigAuditRecord : AggregateRoot<Guid>
{
    private GameConfigAuditRecord()
    {
    }

    private GameConfigAuditRecord(
        Guid id,
        GameConfigAuditAction action,
        GameConfigSetKey configurationSetKey,
        string? environmentName,
        Guid? sourceDraftId,
        long? sourceDraftRevision,
        Guid? previousVersionId,
        Guid targetVersionId,
        string? reason,
        Guid actorAdministratorId,
        DateTime occurredAtUtc,
        string? ipAddress,
        string? userAgent,
        string? correlationId)
    {
        if (id == Guid.Empty || targetVersionId == Guid.Empty || actorAdministratorId == Guid.Empty)
        {
            throw new ArgumentException("Audit, version, and administrator identifiers cannot be empty.");
        }

        if (occurredAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The audit timestamp must use UTC.", nameof(occurredAtUtc));
        }

        Id = id;
        Action = action;
        ConfigurationSetKey = configurationSetKey?.Value ??
            throw new ArgumentNullException(nameof(configurationSetKey));
        EnvironmentName = environmentName;
        SourceDraftId = sourceDraftId;
        SourceDraftRevision = sourceDraftRevision;
        PreviousVersionId = previousVersionId;
        TargetVersionId = targetVersionId;
        Reason = reason;
        ActorAdministratorId = actorAdministratorId;
        OccurredAtUtc = occurredAtUtc;
        IpAddress = NormalizeMetadata(ipAddress, 64);
        UserAgent = NormalizeMetadata(userAgent, 512);
        CorrelationId = NormalizeMetadata(correlationId, 128);
    }

    public GameConfigAuditAction Action { get; private set; }

    public string ConfigurationSetKey { get; private set; } = string.Empty;

    public string? EnvironmentName { get; private set; }

    public Guid? SourceDraftId { get; private set; }

    public long? SourceDraftRevision { get; private set; }

    public Guid? PreviousVersionId { get; private set; }

    public Guid TargetVersionId { get; private set; }

    public string? Reason { get; private set; }

    public Guid ActorAdministratorId { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    public string? IpAddress { get; private set; }

    public string? UserAgent { get; private set; }

    public string? CorrelationId { get; private set; }

    public static GameConfigAuditRecord CreatePublication(
        Guid id,
        GameConfigSetKey configurationSetKey,
        Guid sourceDraftId,
        long sourceDraftRevision,
        Guid publishedVersionId,
        Guid actorAdministratorId,
        DateTime occurredAtUtc,
        string? ipAddress = null,
        string? userAgent = null,
        string? correlationId = null)
    {
        if (sourceDraftId == Guid.Empty || sourceDraftRevision <= 0)
        {
            throw new ArgumentException("Publication audits require a valid source draft and revision.");
        }

        return new GameConfigAuditRecord(
            id,
            GameConfigAuditAction.Published,
            configurationSetKey,
            environmentName: null,
            sourceDraftId,
            sourceDraftRevision,
            previousVersionId: null,
            publishedVersionId,
            reason: null,
            actorAdministratorId,
            occurredAtUtc,
            ipAddress,
            userAgent,
            correlationId);
    }

    public static GameConfigAuditRecord CreateActivation(
        Guid id,
        GameConfigSetKey configurationSetKey,
        GameConfigEnvironmentName environmentName,
        Guid? previousVersionId,
        Guid targetVersionId,
        Guid actorAdministratorId,
        DateTime occurredAtUtc,
        string? reason = null,
        string? ipAddress = null,
        string? userAgent = null,
        string? correlationId = null)
    {
        if (previousVersionId == targetVersionId)
        {
            throw new ArgumentException("An unchanged active version must not produce an activation audit.", nameof(targetVersionId));
        }

        return new GameConfigAuditRecord(
            id,
            GameConfigAuditAction.Activated,
            configurationSetKey,
            environmentName?.Value ?? throw new ArgumentNullException(nameof(environmentName)),
            sourceDraftId: null,
            sourceDraftRevision: null,
            previousVersionId,
            targetVersionId,
            NormalizeOptionalReason(reason),
            actorAdministratorId,
            occurredAtUtc,
            ipAddress,
            userAgent,
            correlationId);
    }

    public static GameConfigAuditRecord CreateRollback(
        Guid id,
        GameConfigSetKey configurationSetKey,
        GameConfigEnvironmentName environmentName,
        Guid previousVersionId,
        Guid targetVersionId,
        string reason,
        Guid actorAdministratorId,
        DateTime occurredAtUtc,
        string? ipAddress = null,
        string? userAgent = null,
        string? correlationId = null)
    {
        if (previousVersionId == Guid.Empty || previousVersionId == targetVersionId)
        {
            throw new ArgumentException("Rollback audits require distinct source and target versions.");
        }

        var normalizedReason = reason?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedReason) ||
            normalizedReason.Length > GameConfigConstants.RollbackReasonMaximumLength)
        {
            throw new ArgumentException(
                $"Rollback reasons must contain 1 to {GameConfigConstants.RollbackReasonMaximumLength} characters.",
                nameof(reason));
        }

        return new GameConfigAuditRecord(
            id,
            GameConfigAuditAction.RolledBack,
            configurationSetKey,
            environmentName?.Value ?? throw new ArgumentNullException(nameof(environmentName)),
            sourceDraftId: null,
            sourceDraftRevision: null,
            previousVersionId,
            targetVersionId,
            normalizedReason,
            actorAdministratorId,
            occurredAtUtc,
            ipAddress,
            userAgent,
            correlationId);
    }

    private static string? NormalizeMetadata(string? value, int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }

    private static string? NormalizeOptionalReason(string? value)
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
