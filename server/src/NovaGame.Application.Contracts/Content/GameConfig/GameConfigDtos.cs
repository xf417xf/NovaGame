namespace NovaGame.Application.Contracts.Content.GameConfig;

public enum GameConfigDraftState
{
    Editing = 1,
    ValidationFailed = 2,
    Validated = 3,
    Published = 4
}

public sealed record GameConfigPageDto<T>(
    IReadOnlyList<T> Items,
    long TotalCount,
    int Page,
    int PageSize);

public sealed record GameConfigValidationIssueDto(
    string Code,
    string Location,
    string Message,
    int? Line = null,
    int? Column = null);

public sealed record GameConfigValidationResultDto(
    Guid DraftId,
    long Revision,
    bool IsValid,
    DateTime ValidatedAtUtc,
    Guid ValidatedByAdministratorId,
    IReadOnlyList<GameConfigValidationIssueDto> Issues);

public sealed record GameConfigDraftSummaryDto(
    Guid Id,
    string ConfigurationSet,
    string Name,
    string SchemaVersion,
    long Revision,
    GameConfigDraftState State,
    Guid? BaseVersionId,
    Guid? PublishedVersionId,
    DateTime CreatedAtUtc,
    Guid CreatedByAdministratorId,
    DateTime UpdatedAtUtc,
    Guid UpdatedByAdministratorId);

public sealed record GameConfigDraftDto(
    Guid Id,
    string ConfigurationSet,
    string Name,
    string SchemaVersion,
    long Revision,
    GameConfigDraftState State,
    Guid? BaseVersionId,
    Guid? PublishedVersionId,
    string Content,
    GameConfigValidationResultDto? LatestValidation,
    DateTime CreatedAtUtc,
    Guid CreatedByAdministratorId,
    DateTime UpdatedAtUtc,
    Guid UpdatedByAdministratorId);

public sealed record GameConfigVersionSummaryDto(
    Guid Id,
    string ConfigurationSet,
    string SchemaVersion,
    Guid SourceDraftId,
    long SourceDraftRevision,
    string ReleaseNotes,
    string ContentHash,
    string SnapshotHash,
    long SnapshotLength,
    DateTime PublishedAtUtc,
    Guid PublishedByAdministratorId);

public sealed record GameConfigVersionDto(
    Guid Id,
    string ConfigurationSet,
    string SchemaVersion,
    Guid SourceDraftId,
    long SourceDraftRevision,
    string ReleaseNotes,
    string ContentHash,
    string SnapshotHash,
    long SnapshotLength,
    string Content,
    DateTime PublishedAtUtc,
    Guid PublishedByAdministratorId);

public sealed record GameConfigEnvironmentDto(
    string ConfigurationSet,
    string Environment,
    long Revision,
    Guid ActiveVersionId,
    Guid? PreviousVersionId,
    string SchemaVersion,
    string ContentHash,
    DateTime ActivatedAtUtc,
    Guid ActivatedByAdministratorId,
    string? Reason);

public sealed record GameConfigSnapshotDownload(
    byte[] Content,
    string SnapshotHash,
    string FileName);
