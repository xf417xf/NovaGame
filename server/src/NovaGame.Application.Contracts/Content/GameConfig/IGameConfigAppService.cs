namespace NovaGame.Application.Contracts.Content.GameConfig;

public interface IGameConfigAppService
{
    Task<GameConfigPageDto<GameConfigDraftSummaryDto>> ListDraftsAsync(
        ListGameConfigDraftsRequest request,
        CancellationToken cancellationToken = default);

    Task<GameConfigDraftDto> CreateDraftAsync(
        CreateGameConfigDraftRequest request,
        CancellationToken cancellationToken = default);

    Task<GameConfigDraftDto> GetDraftAsync(
        Guid draftId,
        CancellationToken cancellationToken = default);

    Task<GameConfigDraftDto> UpdateDraftAsync(
        Guid draftId,
        UpdateGameConfigDraftRequest request,
        CancellationToken cancellationToken = default);

    Task<GameConfigValidationResultDto> ValidateDraftAsync(
        Guid draftId,
        ValidateGameConfigDraftRequest request,
        CancellationToken cancellationToken = default);

    Task<GameConfigVersionDto> PublishDraftAsync(
        Guid draftId,
        PublishGameConfigDraftRequest request,
        CancellationToken cancellationToken = default);

    Task<GameConfigPageDto<GameConfigVersionSummaryDto>> ListVersionsAsync(
        ListGameConfigVersionsRequest request,
        CancellationToken cancellationToken = default);

    Task<GameConfigVersionDto> GetVersionAsync(
        Guid versionId,
        CancellationToken cancellationToken = default);

    Task<GameConfigSnapshotDownload> DownloadVersionSnapshotAsync(
        Guid versionId,
        CancellationToken cancellationToken = default);

    Task<GameConfigEnvironmentDto> GetEnvironmentAsync(
        string environmentName,
        string configurationSet,
        CancellationToken cancellationToken = default);

    Task<GameConfigEnvironmentDto> ActivateVersionAsync(
        string environmentName,
        ActivateGameConfigVersionRequest request,
        CancellationToken cancellationToken = default);

    Task<GameConfigEnvironmentDto> RollbackEnvironmentAsync(
        string environmentName,
        RollbackGameConfigEnvironmentRequest request,
        CancellationToken cancellationToken = default);

    Task<GameConfigSnapshotDownload> DownloadActiveSnapshotAsync(
        string environmentName,
        string configurationSet,
        CancellationToken cancellationToken = default);
}
