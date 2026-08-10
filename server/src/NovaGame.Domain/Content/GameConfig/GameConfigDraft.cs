using NovaCore.BuildingBlocks.Domain;
using NovaGame.Domain.Shared.Content.GameConfig;

namespace NovaGame.Domain.Content.GameConfig;

public sealed class GameConfigDraft : AggregateRoot<Guid>
{
    private readonly List<GameConfigValidationIssue> _validationIssues = [];

    private GameConfigDraft()
    {
    }

    public GameConfigDraft(
        Guid id,
        GameConfigSetKey configurationSetKey,
        string name,
        Guid? baseVersionId,
        string schemaVersion,
        string rawContentJson,
        Guid createdByAdministratorId,
        DateTime createdAtUtc)
    {
        if (id == Guid.Empty || createdByAdministratorId == Guid.Empty)
        {
            throw new ArgumentException("Draft and administrator identifiers cannot be empty.");
        }

        if (baseVersionId == Guid.Empty)
        {
            throw new ArgumentException("A base-version identifier cannot be empty.", nameof(baseVersionId));
        }

        Id = id;
        ConfigurationSetKey = configurationSetKey?.Value ??
            throw new ArgumentNullException(nameof(configurationSetKey));
        Name = NormalizeName(name);
        BaseVersionId = baseVersionId;
        SchemaVersion = NormalizeSchemaVersion(schemaVersion);
        RawContentJson = RequireRawContent(rawContentJson);
        Revision = 1;
        CreatedByAdministratorId = createdByAdministratorId;
        CreatedAtUtc = RequireUtc(createdAtUtc, nameof(createdAtUtc));
        UpdatedByAdministratorId = createdByAdministratorId;
        UpdatedAtUtc = createdAtUtc;
        ValidationStatus = GameConfigValidationStatus.NotValidated;
    }

    public string ConfigurationSetKey { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public Guid? BaseVersionId { get; private set; }

    public string SchemaVersion { get; private set; } = string.Empty;

    public string RawContentJson { get; private set; } = string.Empty;

    public long Revision { get; private set; }

    public Guid CreatedByAdministratorId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public Guid UpdatedByAdministratorId { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public GameConfigValidationStatus ValidationStatus { get; private set; }

    public long? LastValidatedRevision { get; private set; }

    public string? ValidationProfile { get; private set; }

    public Guid? LastValidatedByAdministratorId { get; private set; }

    public DateTime? LastValidatedAtUtc { get; private set; }

    public IReadOnlyCollection<GameConfigValidationIssue> ValidationIssues => _validationIssues.AsReadOnly();

    public Guid? PublishedVersionId { get; private set; }

    public bool IsPublished => PublishedVersionId.HasValue;

    public void Update(
        long expectedRevision,
        string name,
        string schemaVersion,
        string rawContentJson,
        Guid updatedByAdministratorId,
        DateTime updatedAtUtc)
    {
        EnsureEditable();
        EnsureRevision(expectedRevision);
        if (updatedByAdministratorId == Guid.Empty)
        {
            throw new ArgumentException("The updating administrator identifier cannot be empty.", nameof(updatedByAdministratorId));
        }

        var normalizedName = NormalizeName(name);
        var normalizedSchemaVersion = NormalizeSchemaVersion(schemaVersion);
        var requiredContent = RequireRawContent(rawContentJson);
        var requiredUpdatedAtUtc = RequireUtc(updatedAtUtc, nameof(updatedAtUtc));

        Name = normalizedName;
        SchemaVersion = normalizedSchemaVersion;
        RawContentJson = requiredContent;
        Revision = checked(Revision + 1);
        UpdatedByAdministratorId = updatedByAdministratorId;
        UpdatedAtUtc = requiredUpdatedAtUtc;
        ClearValidation();
    }

    public void RecordValidation(
        long expectedRevision,
        GameConfigValidationResult result,
        Guid validatedByAdministratorId,
        DateTime validatedAtUtc)
    {
        EnsureEditable();
        EnsureRevision(expectedRevision);
        ArgumentNullException.ThrowIfNull(result);
        if (validatedByAdministratorId == Guid.Empty)
        {
            throw new ArgumentException("The validating administrator identifier cannot be empty.", nameof(validatedByAdministratorId));
        }

        if (!string.Equals(result.SchemaVersion, SchemaVersion, StringComparison.Ordinal))
        {
            throw new ArgumentException("The validation result does not belong to the current draft schema version.", nameof(result));
        }

        ValidationStatus = result.IsValid
            ? GameConfigValidationStatus.Valid
            : GameConfigValidationStatus.Invalid;
        LastValidatedRevision = Revision;
        ValidationProfile = result.ValidationProfile;
        LastValidatedByAdministratorId = validatedByAdministratorId;
        LastValidatedAtUtc = RequireUtc(validatedAtUtc, nameof(validatedAtUtc));
        _validationIssues.Clear();
        _validationIssues.AddRange(result.Issues.Select((issue, index) => issue.Copy(index)));
    }

    public void EnsurePublishable(long expectedRevision)
    {
        EnsureEditable();
        EnsureRevision(expectedRevision);
        if (ValidationStatus != GameConfigValidationStatus.Valid ||
            LastValidatedRevision != Revision ||
            !string.Equals(
                ValidationProfile,
                GameConfigConstants.ValidationProfile,
                StringComparison.Ordinal))
        {
            throw new GameConfigDomainException(
                GameConfigErrorCodes.DraftNotValidated,
                "The current draft revision must pass authoritative validation before publication.");
        }
    }

    public void MarkPublished(long expectedRevision, Guid publishedVersionId)
    {
        if (publishedVersionId == Guid.Empty)
        {
            throw new ArgumentException("The published-version identifier cannot be empty.", nameof(publishedVersionId));
        }

        EnsurePublishable(expectedRevision);
        PublishedVersionId = publishedVersionId;
    }

    private void EnsureEditable()
    {
        if (PublishedVersionId.HasValue)
        {
            throw new GameConfigDomainException(
                GameConfigErrorCodes.DraftRevisionAlreadyPublished,
                $"Draft revision {Revision} has already been published as version {PublishedVersionId.Value}.");
        }
    }

    private void EnsureRevision(long expectedRevision)
    {
        if (expectedRevision != Revision)
        {
            throw new GameConfigDomainException(
                GameConfigErrorCodes.DraftRevisionConflict,
                $"Draft revision {expectedRevision} is stale; the current revision is {Revision}.");
        }
    }

    private void ClearValidation()
    {
        ValidationStatus = GameConfigValidationStatus.NotValidated;
        LastValidatedRevision = null;
        ValidationProfile = null;
        LastValidatedByAdministratorId = null;
        LastValidatedAtUtc = null;
        _validationIssues.Clear();
    }

    private static string NormalizeName(string value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Length > GameConfigConstants.DraftNameMaximumLength)
        {
            throw new ArgumentException(
                $"Draft names must contain 1 to {GameConfigConstants.DraftNameMaximumLength} characters.",
                nameof(value));
        }

        return normalized;
    }

    private static string NormalizeSchemaVersion(string value)
    {
        if (!GameConfigSchemaVersionRules.IsValid(value))
        {
            throw new ArgumentException(
                "Schema versions must be safe identifiers containing 1 to " +
                $"{GameConfigConstants.SchemaVersionMaximumLength} characters.",
                nameof(value));
        }

        return value;
    }

    private static string RequireRawContent(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > GameConfigConstants.RawContentMaximumLength)
        {
            throw new ArgumentException(
                $"Draft content cannot exceed {GameConfigConstants.RawContentMaximumLength} characters.",
                nameof(value));
        }

        return value;
    }

    private static DateTime RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The timestamp must use UTC.", parameterName);
        }

        return value;
    }
}
