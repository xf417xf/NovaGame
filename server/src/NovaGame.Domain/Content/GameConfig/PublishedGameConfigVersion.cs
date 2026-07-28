using NovaCore.BuildingBlocks.Domain;
using NovaGame.Domain.Shared.Content.GameConfig;

namespace NovaGame.Domain.Content.GameConfig;

public sealed class PublishedGameConfigVersion : AggregateRoot<Guid>
{
    private PublishedGameConfigVersion()
    {
    }

    public PublishedGameConfigVersion(
        Guid id,
        GameConfigSetKey configurationSetKey,
        Guid sourceDraftId,
        long sourceDraftRevision,
        string releaseNotes,
        GameConfigProcessedContent content,
        GameConfigSnapshot snapshot,
        Guid publishedByAdministratorId,
        DateTime publishedAtUtc)
    {
        if (id == Guid.Empty || sourceDraftId == Guid.Empty || publishedByAdministratorId == Guid.Empty)
        {
            throw new ArgumentException("Published-version, draft, and administrator identifiers cannot be empty.");
        }

        if (sourceDraftRevision <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceDraftRevision));
        }

        ArgumentNullException.ThrowIfNull(configurationSetKey);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(snapshot);
        var normalizedReleaseNotes = releaseNotes?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedReleaseNotes) ||
            normalizedReleaseNotes.Length > GameConfigConstants.ReleaseNotesMaximumLength)
        {
            throw new ArgumentException(
                $"Release notes must contain 1 to {GameConfigConstants.ReleaseNotesMaximumLength} characters.",
                nameof(releaseNotes));
        }

        if (publishedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The publication timestamp must use UTC.", nameof(publishedAtUtc));
        }

        if (snapshot.ConfigurationVersionId != id ||
            snapshot.ConfigurationSetKey != configurationSetKey.Value ||
            snapshot.SchemaVersion != content.SchemaVersion ||
            snapshot.CreatedAtUtc != publishedAtUtc)
        {
            throw new ArgumentException("The snapshot metadata does not match the published version.", nameof(snapshot));
        }

        Id = id;
        ConfigurationSetKey = configurationSetKey.Value;
        SchemaVersion = content.SchemaVersion;
        SourceDraftId = sourceDraftId;
        SourceDraftRevision = sourceDraftRevision;
        ReleaseNotes = normalizedReleaseNotes;
        CanonicalContentJson = content.CanonicalJson;
        ContentHash = snapshot.ContentHash;
        SnapshotHash = snapshot.SnapshotHash;
        SnapshotJson = snapshot.Json;
        PublishedByAdministratorId = publishedByAdministratorId;
        PublishedAtUtc = publishedAtUtc;
    }

    public string ConfigurationSetKey { get; private set; } = string.Empty;

    public string SchemaVersion { get; private set; } = string.Empty;

    public Guid SourceDraftId { get; private set; }

    public long SourceDraftRevision { get; private set; }

    public string ReleaseNotes { get; private set; } = string.Empty;

    public string CanonicalContentJson { get; private set; } = string.Empty;

    public string ContentHash { get; private set; } = string.Empty;

    public string SnapshotHash { get; private set; } = string.Empty;

    public string SnapshotJson { get; private set; } = string.Empty;

    public Guid PublishedByAdministratorId { get; private set; }

    public DateTime PublishedAtUtc { get; private set; }
}
