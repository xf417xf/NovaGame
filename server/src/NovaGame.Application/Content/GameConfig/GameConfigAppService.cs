using System.Security.Claims;
using System.Text;
using NovaCore.BuildingBlocks.CurrentUser;
using NovaCore.BuildingBlocks.Infrastructure;
using NovaCore.BuildingBlocks.Permissions;
using NovaCore.BuildingBlocks.Timing;
using NovaCore.BuildingBlocks.UnitOfWork;
using NovaGame.Application.Auditing;
using NovaGame.Application.Contracts.Content.GameConfig;
using NovaGame.Domain.Content.GameConfig;
using NovaGame.Domain.Shared.Content.GameConfig;

namespace NovaGame.Application.Content.GameConfig;

public sealed class GameConfigAppService : IGameConfigAppService
{
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IGameConfigDraftRepository _draftRepository;
    private readonly IPublishedGameConfigVersionRepository _versionRepository;
    private readonly IGameConfigEnvironmentRepository _environmentRepository;
    private readonly IGameConfigAuditRepository _auditRepository;
    private readonly GameConfigContentProcessor _contentProcessor;
    private readonly GameConfigSnapshotBuilder _snapshotBuilder;
    private readonly IPermissionChecker _permissionChecker;
    private readonly IRequestAuditContextAccessor _requestAuditContextAccessor;
    private readonly ICurrentUser _currentUser;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IClock _clock;

    public GameConfigAppService(
        IUnitOfWorkManager unitOfWorkManager,
        IGameConfigDraftRepository draftRepository,
        IPublishedGameConfigVersionRepository versionRepository,
        IGameConfigEnvironmentRepository environmentRepository,
        IGameConfigAuditRepository auditRepository,
        GameConfigContentProcessor contentProcessor,
        GameConfigSnapshotBuilder snapshotBuilder,
        IPermissionChecker permissionChecker,
        IRequestAuditContextAccessor requestAuditContextAccessor,
        ICurrentUser currentUser,
        IGuidGenerator guidGenerator,
        IClock clock)
    {
        _unitOfWorkManager = unitOfWorkManager;
        _draftRepository = draftRepository;
        _versionRepository = versionRepository;
        _environmentRepository = environmentRepository;
        _auditRepository = auditRepository;
        _contentProcessor = contentProcessor;
        _snapshotBuilder = snapshotBuilder;
        _permissionChecker = permissionChecker;
        _requestAuditContextAccessor = requestAuditContextAccessor;
        _currentUser = currentUser;
        _guidGenerator = guidGenerator;
        _clock = clock;
    }

    public async Task<GameConfigPageDto<GameConfigDraftSummaryDto>> ListDraftsAsync(
        ListGameConfigDraftsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await RequirePermissionAsync(GameConfigPermissions.Access, cancellationToken);

        var configurationSet = ParseConfigurationSet(request.ConfigurationSet);
        var state = request.State switch
        {
            null => GameConfigDraftStateFilter.All,
            GameConfigDraftState.Editing => GameConfigDraftStateFilter.Editable,
            GameConfigDraftState.ValidationFailed => GameConfigDraftStateFilter.Invalid,
            GameConfigDraftState.Validated => GameConfigDraftStateFilter.Validated,
            GameConfigDraftState.Published => GameConfigDraftStateFilter.Published,
            _ => throw InvalidRequest("The requested draft state is invalid.")
        };
        var search = NormalizeOptional(request.Search);
        var skipCount = GetSkipCount(request.Page, request.PageSize);
        var drafts = await _draftRepository.GetListAsync(
            configurationSet.Value,
            search,
            state,
            skipCount,
            request.PageSize,
            cancellationToken);
        var totalCount = await _draftRepository.GetCountAsync(
            configurationSet.Value,
            search,
            state,
            cancellationToken);

        return new GameConfigPageDto<GameConfigDraftSummaryDto>(
            drafts.Select(MapDraftSummary).ToArray(),
            totalCount,
            request.Page,
            request.PageSize);
    }

    public async Task<GameConfigDraftDto> CreateDraftAsync(
        CreateGameConfigDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actorId = await RequirePermissionAsync(
            GameConfigPermissions.ManageDrafts,
            cancellationToken);
        var configurationSet = ParseConfigurationSet(request.ConfigurationSet);
        var now = GetNowUtc();

        await using var unitOfWork = await _unitOfWorkManager.BeginAsync(
            cancellationToken: cancellationToken);
        if (request.BaseVersionId.HasValue)
        {
            var baseVersion = await GetVersionOrThrowAsync(
                request.BaseVersionId.Value,
                cancellationToken);
            EnsureConfigurationSetMatches(configurationSet, baseVersion);
            if (!string.Equals(baseVersion.SchemaVersion, request.SchemaVersion.Trim(), StringComparison.Ordinal))
            {
                throw new GameConfigException(
                    GameConfigFailureKind.UnprocessableEntity,
                    GameConfigErrorCodes.SchemaIncompatible,
                    "The base version uses a different schema version.");
            }
        }

        var draft = TranslateDomain(() => new GameConfigDraft(
            _guidGenerator.Create(),
            configurationSet,
            request.Name,
            request.BaseVersionId,
            request.SchemaVersion,
            request.Content,
            actorId,
            now));
        await _draftRepository.AddAsync(draft, cancellationToken);
        await unitOfWork.CompleteAsync(cancellationToken);
        return MapDraft(draft);
    }

    public async Task<GameConfigDraftDto> GetDraftAsync(
        Guid draftId,
        CancellationToken cancellationToken = default)
    {
        await RequirePermissionAsync(GameConfigPermissions.Access, cancellationToken);
        var draft = await GetDraftOrThrowAsync(draftId, forUpdate: false, cancellationToken);
        return MapDraft(draft);
    }

    public async Task<GameConfigDraftDto> UpdateDraftAsync(
        Guid draftId,
        UpdateGameConfigDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actorId = await RequirePermissionAsync(
            GameConfigPermissions.ManageDrafts,
            cancellationToken);

        await using var unitOfWork = await _unitOfWorkManager.BeginAsync(
            cancellationToken: cancellationToken);
        var draft = await GetDraftOrThrowAsync(draftId, forUpdate: true, cancellationToken);
        TranslateDomain(() => draft.Update(
            request.ExpectedRevision,
            request.Name,
            request.SchemaVersion,
            request.Content,
            actorId,
            GetNowUtc()));
        await unitOfWork.CompleteAsync(cancellationToken);
        return MapDraft(draft);
    }

    public async Task<GameConfigValidationResultDto> ValidateDraftAsync(
        Guid draftId,
        ValidateGameConfigDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actorId = await RequirePermissionAsync(
            GameConfigPermissions.ManageDrafts,
            cancellationToken);

        await using var unitOfWork = await _unitOfWorkManager.BeginAsync(
            cancellationToken: cancellationToken);
        var draft = await GetDraftOrThrowAsync(draftId, forUpdate: true, cancellationToken);
        var validation = _contentProcessor.Process(draft.SchemaVersion, draft.RawContentJson).Validation;
        TranslateDomain(() => draft.RecordValidation(
            request.ExpectedRevision,
            validation,
            actorId,
            GetNowUtc()));
        await unitOfWork.CompleteAsync(cancellationToken);
        return MapValidation(draft);
    }

    public async Task<GameConfigVersionDto> PublishDraftAsync(
        Guid draftId,
        PublishGameConfigDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actorId = await RequirePermissionAsync(
            GameConfigPermissions.Publish,
            cancellationToken);

        await using var unitOfWork = await _unitOfWorkManager.BeginAsync(
            cancellationToken: cancellationToken);
        var draft = await GetDraftOrThrowAsync(draftId, forUpdate: true, cancellationToken);
        if (draft.PublishedVersionId.HasValue)
        {
            if (request.ExpectedRevision != draft.Revision)
            {
                throw RevisionConflict(request.ExpectedRevision, draft.Revision);
            }

            var publishedVersion = await GetVersionOrThrowAsync(
                draft.PublishedVersionId.Value,
                cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);
            return MapVersion(publishedVersion);
        }

        TranslateDomain(() => draft.EnsurePublishable(request.ExpectedRevision));
        var processingResult = _contentProcessor.Process(draft.SchemaVersion, draft.RawContentJson);
        if (processingResult.Content is null)
        {
            throw new GameConfigException(
                GameConfigFailureKind.UnprocessableEntity,
                GameConfigErrorCodes.DraftNotValidated,
                "The current draft content no longer passes authoritative validation.",
                ToValidationDetails(processingResult.Validation.Issues));
        }

        var now = GetNowUtc();
        var configurationSet = ParseConfigurationSet(draft.ConfigurationSetKey);
        var versionId = _guidGenerator.Create();
        var snapshot = _snapshotBuilder.Build(
            versionId,
            configurationSet,
            now,
            processingResult.Content);
        var version = TranslateDomain(() => new PublishedGameConfigVersion(
            versionId,
            configurationSet,
            draft.Id,
            draft.Revision,
            request.ReleaseNotes,
            processingResult.Content,
            snapshot,
            actorId,
            now));
        TranslateDomain(() => draft.MarkPublished(request.ExpectedRevision, version.Id));

        await _versionRepository.AddAsync(version, cancellationToken);
        await _auditRepository.AddAsync(
            CreatePublicationAudit(configurationSet, draft, version, actorId, now),
            cancellationToken);
        await unitOfWork.CompleteAsync(cancellationToken);
        return MapVersion(version);
    }

    public async Task<GameConfigPageDto<GameConfigVersionSummaryDto>> ListVersionsAsync(
        ListGameConfigVersionsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await RequirePermissionAsync(GameConfigPermissions.Access, cancellationToken);

        var configurationSet = ParseConfigurationSet(request.ConfigurationSet);
        var schemaVersion = NormalizeOptional(request.SchemaVersion);
        var skipCount = GetSkipCount(request.Page, request.PageSize);
        var versions = await _versionRepository.GetListAsync(
            configurationSet.Value,
            schemaVersion,
            skipCount,
            request.PageSize,
            cancellationToken);
        var totalCount = await _versionRepository.GetCountAsync(
            configurationSet.Value,
            schemaVersion,
            cancellationToken);

        return new GameConfigPageDto<GameConfigVersionSummaryDto>(
            versions.Select(MapVersionSummary).ToArray(),
            totalCount,
            request.Page,
            request.PageSize);
    }

    public async Task<GameConfigVersionDto> GetVersionAsync(
        Guid versionId,
        CancellationToken cancellationToken = default)
    {
        await RequirePermissionAsync(GameConfigPermissions.Access, cancellationToken);
        return MapVersion(await GetVersionOrThrowAsync(versionId, cancellationToken));
    }

    public async Task<GameConfigSnapshotDownload> DownloadVersionSnapshotAsync(
        Guid versionId,
        CancellationToken cancellationToken = default)
    {
        await RequirePermissionAsync(GameConfigPermissions.Access, cancellationToken);
        var version = await GetVersionOrThrowAsync(versionId, cancellationToken);
        return CreateSnapshotDownload(version);
    }

    public async Task<GameConfigEnvironmentDto> GetEnvironmentAsync(
        string environmentName,
        string configurationSet,
        CancellationToken cancellationToken = default)
    {
        await RequirePermissionAsync(GameConfigPermissions.Access, cancellationToken);
        var setKey = ParseConfigurationSet(configurationSet);
        var name = ParseEnvironmentName(environmentName);
        var environment = await GetEnvironmentOrThrowAsync(setKey, name, forUpdate: false, cancellationToken);
        var activeVersion = await GetVersionOrThrowAsync(environment.ActiveVersionId, cancellationToken);
        return MapEnvironment(environment, activeVersion);
    }

    public async Task<GameConfigEnvironmentDto> ActivateVersionAsync(
        string environmentName,
        ActivateGameConfigVersionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actorId = await RequirePermissionAsync(
            GameConfigPermissions.Activate,
            cancellationToken);
        var setKey = ParseConfigurationSet(request.ConfigurationSet);
        var name = ParseEnvironmentName(environmentName);
        EnsureNotEmpty(request.VersionId, "A version identifier is required.");

        await using var unitOfWork = await _unitOfWorkManager.BeginAsync(
            cancellationToken: cancellationToken);
        var targetVersion = await GetVersionOrThrowAsync(request.VersionId, cancellationToken);
        EnsureConfigurationSetMatches(setKey, targetVersion);
        var environment = await _environmentRepository.FindForUpdateAsync(
            setKey.Value,
            name.Value,
            cancellationToken);
        var now = GetNowUtc();
        GameConfigEnvironmentTransition transition;

        if (environment is null)
        {
            if (request.ExpectedRevision != 0)
            {
                throw ActivationConflict(request.ExpectedRevision, currentRevision: 0);
            }

            environment = TranslateDomain(() => new GameConfigEnvironment(
                _guidGenerator.Create(),
                setKey,
                name,
                targetVersion.Id,
                actorId,
                now,
                request.Reason));
            transition = new GameConfigEnvironmentTransition(
                PreviousVersionId: null,
                targetVersion.Id,
                environment.Revision,
                Changed: true,
                environment.Reason);
            await _environmentRepository.AddAsync(environment, cancellationToken);
        }
        else
        {
            transition = TranslateDomain(() => environment.Activate(
                targetVersion.Id,
                request.ExpectedRevision,
                actorId,
                now,
                request.Reason));
        }

        if (transition.Changed)
        {
            await _auditRepository.AddAsync(
                CreateActivationAudit(
                    setKey,
                    name,
                    transition,
                    actorId,
                    now),
                cancellationToken);
        }

        await unitOfWork.CompleteAsync(cancellationToken);
        return MapEnvironment(environment, targetVersion);
    }

    public async Task<GameConfigEnvironmentDto> RollbackEnvironmentAsync(
        string environmentName,
        RollbackGameConfigEnvironmentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actorId = await RequirePermissionAsync(
            GameConfigPermissions.Activate,
            cancellationToken);
        var setKey = ParseConfigurationSet(request.ConfigurationSet);
        var name = ParseEnvironmentName(environmentName);
        EnsureNotEmpty(request.TargetVersionId, "A rollback target version identifier is required.");

        await using var unitOfWork = await _unitOfWorkManager.BeginAsync(
            cancellationToken: cancellationToken);
        var environment = await GetEnvironmentOrThrowAsync(setKey, name, forUpdate: true, cancellationToken);
        var activeVersion = await GetVersionOrThrowAsync(environment.ActiveVersionId, cancellationToken);
        var targetVersion = await GetVersionOrThrowAsync(request.TargetVersionId, cancellationToken);
        EnsureConfigurationSetMatches(setKey, targetVersion);

        if (targetVersion.Id == activeVersion.Id ||
            targetVersion.PublishedAtUtc >= activeVersion.PublishedAtUtc)
        {
            throw new GameConfigException(
                GameConfigFailureKind.UnprocessableEntity,
                GameConfigErrorCodes.InvalidRollbackTarget,
                "Rollback requires an older published version from the same configuration set.");
        }

        if (!string.Equals(
                targetVersion.SchemaVersion,
                activeVersion.SchemaVersion,
                StringComparison.Ordinal))
        {
            throw new GameConfigException(
                GameConfigFailureKind.UnprocessableEntity,
                GameConfigErrorCodes.SchemaIncompatible,
                "Rollback requires a version with the same schema version as the active version.");
        }

        var now = GetNowUtc();
        var transition = TranslateDomain(() => environment.Activate(
            targetVersion.Id,
            request.ExpectedRevision,
            actorId,
            now,
            request.Reason));
        if (!transition.Changed)
        {
            throw new GameConfigException(
                GameConfigFailureKind.UnprocessableEntity,
                GameConfigErrorCodes.InvalidRollbackTarget,
                "The rollback target is already active.");
        }

        await _auditRepository.AddAsync(
            CreateRollbackAudit(
                setKey,
                name,
                activeVersion.Id,
                targetVersion.Id,
                request.Reason,
                actorId,
                now),
            cancellationToken);
        await unitOfWork.CompleteAsync(cancellationToken);
        return MapEnvironment(environment, targetVersion);
    }

    public async Task<GameConfigSnapshotDownload> DownloadActiveSnapshotAsync(
        string environmentName,
        string configurationSet,
        CancellationToken cancellationToken = default)
    {
        var setKey = ParseConfigurationSet(configurationSet);
        var name = ParseEnvironmentName(environmentName);
        var environment = await GetEnvironmentOrThrowAsync(setKey, name, forUpdate: false, cancellationToken);
        var version = await GetVersionOrThrowAsync(environment.ActiveVersionId, cancellationToken);
        return CreateSnapshotDownload(version);
    }

    private async Task<Guid> RequirePermissionAsync(
        string permissionName,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || !TryGetCurrentAdministratorId(out var administratorId))
        {
            throw new GameConfigException(
                GameConfigFailureKind.Unauthorized,
                GameConfigErrorCodes.Unauthorized,
                "Authentication is required.");
        }

        if (!await _permissionChecker.IsGrantedAsync(permissionName, cancellationToken))
        {
            throw new GameConfigException(
                GameConfigFailureKind.Forbidden,
                GameConfigErrorCodes.Forbidden,
                "The current administrator does not have permission to perform this action.");
        }

        return administratorId;
    }

    private bool TryGetCurrentAdministratorId(out Guid administratorId)
    {
        var value = _currentUser.Id ??
            _currentUser.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out administratorId);
    }

    private async Task<GameConfigDraft> GetDraftOrThrowAsync(
        Guid draftId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        EnsureNotEmpty(draftId, "A draft identifier is required.");
        var draft = forUpdate
            ? await _draftRepository.FindByIdForUpdateAsync(draftId, cancellationToken)
            : await _draftRepository.FindByIdAsync(draftId, cancellationToken);
        return draft ?? throw new GameConfigException(
            GameConfigFailureKind.NotFound,
            GameConfigErrorCodes.DraftNotFound,
            $"Game-configuration draft '{draftId}' was not found.");
    }

    private async Task<PublishedGameConfigVersion> GetVersionOrThrowAsync(
        Guid versionId,
        CancellationToken cancellationToken)
    {
        EnsureNotEmpty(versionId, "A published-version identifier is required.");
        return await _versionRepository.FindByIdAsync(versionId, cancellationToken) ??
            throw new GameConfigException(
                GameConfigFailureKind.NotFound,
                GameConfigErrorCodes.PublishedVersionNotFound,
                $"Published game-configuration version '{versionId}' was not found.");
    }

    private async Task<GameConfigEnvironment> GetEnvironmentOrThrowAsync(
        GameConfigSetKey configurationSet,
        GameConfigEnvironmentName environmentName,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        var environment = forUpdate
            ? await _environmentRepository.FindForUpdateAsync(
                configurationSet.Value,
                environmentName.Value,
                cancellationToken)
            : await _environmentRepository.FindAsync(
                configurationSet.Value,
                environmentName.Value,
                cancellationToken);
        return environment ?? throw new GameConfigException(
            GameConfigFailureKind.NotFound,
            GameConfigErrorCodes.ActiveVersionNotFound,
            $"Environment '{environmentName.Value}' has no active version for configuration set '{configurationSet.Value}'.");
    }

    private GameConfigSnapshotDownload CreateSnapshotDownload(PublishedGameConfigVersion version)
    {
        if (!_snapshotBuilder.Verify(version.SnapshotJson, version.SnapshotHash))
        {
            throw new GameConfigException(
                GameConfigFailureKind.InternalError,
                GameConfigErrorCodes.SnapshotIntegrityInvalid,
                "The stored game-configuration snapshot failed its integrity check.");
        }

        return new GameConfigSnapshotDownload(
            Encoding.UTF8.GetBytes(version.SnapshotJson),
            version.SnapshotHash,
            $"{version.ConfigurationSetKey}-{version.Id:D}.json");
    }

    private GameConfigAuditRecord CreatePublicationAudit(
        GameConfigSetKey configurationSet,
        GameConfigDraft draft,
        PublishedGameConfigVersion version,
        Guid actorId,
        DateTime occurredAtUtc)
    {
        var context = _requestAuditContextAccessor.Current;
        return GameConfigAuditRecord.CreatePublication(
            _guidGenerator.Create(),
            configurationSet,
            draft.Id,
            draft.Revision,
            version.Id,
            actorId,
            occurredAtUtc,
            context.IpAddress,
            context.UserAgent,
            context.CorrelationId);
    }

    private GameConfigAuditRecord CreateActivationAudit(
        GameConfigSetKey configurationSet,
        GameConfigEnvironmentName environmentName,
        GameConfigEnvironmentTransition transition,
        Guid actorId,
        DateTime occurredAtUtc)
    {
        var context = _requestAuditContextAccessor.Current;
        return GameConfigAuditRecord.CreateActivation(
            _guidGenerator.Create(),
            configurationSet,
            environmentName,
            transition.PreviousVersionId,
            transition.ActiveVersionId,
            actorId,
            occurredAtUtc,
            transition.Reason,
            context.IpAddress,
            context.UserAgent,
            context.CorrelationId);
    }

    private GameConfigAuditRecord CreateRollbackAudit(
        GameConfigSetKey configurationSet,
        GameConfigEnvironmentName environmentName,
        Guid previousVersionId,
        Guid targetVersionId,
        string reason,
        Guid actorId,
        DateTime occurredAtUtc)
    {
        var context = _requestAuditContextAccessor.Current;
        return GameConfigAuditRecord.CreateRollback(
            _guidGenerator.Create(),
            configurationSet,
            environmentName,
            previousVersionId,
            targetVersionId,
            reason,
            actorId,
            occurredAtUtc,
            context.IpAddress,
            context.UserAgent,
            context.CorrelationId);
    }

    private static GameConfigDraftSummaryDto MapDraftSummary(GameConfigDraft draft)
    {
        return new GameConfigDraftSummaryDto(
            draft.Id,
            draft.ConfigurationSetKey,
            draft.Name,
            draft.SchemaVersion,
            draft.Revision,
            GetDraftState(draft),
            draft.BaseVersionId,
            draft.PublishedVersionId,
            draft.CreatedAtUtc,
            draft.CreatedByAdministratorId,
            draft.UpdatedAtUtc,
            draft.UpdatedByAdministratorId);
    }

    private static GameConfigDraftDto MapDraft(GameConfigDraft draft)
    {
        return new GameConfigDraftDto(
            draft.Id,
            draft.ConfigurationSetKey,
            draft.Name,
            draft.SchemaVersion,
            draft.Revision,
            GetDraftState(draft),
            draft.BaseVersionId,
            draft.PublishedVersionId,
            draft.RawContentJson,
            draft.LastValidatedAtUtc.HasValue ? MapValidation(draft) : null,
            draft.CreatedAtUtc,
            draft.CreatedByAdministratorId,
            draft.UpdatedAtUtc,
            draft.UpdatedByAdministratorId);
    }

    private static GameConfigValidationResultDto MapValidation(GameConfigDraft draft)
    {
        if (!draft.LastValidatedAtUtc.HasValue ||
            !draft.LastValidatedByAdministratorId.HasValue ||
            !draft.LastValidatedRevision.HasValue)
        {
            throw new InvalidOperationException("The draft does not have a complete validation result.");
        }

        return new GameConfigValidationResultDto(
            draft.Id,
            draft.LastValidatedRevision.Value,
            draft.ValidationStatus == GameConfigValidationStatus.Valid,
            draft.LastValidatedAtUtc.Value,
            draft.LastValidatedByAdministratorId.Value,
            draft.ValidationIssues
                .OrderBy(issue => issue.Sequence)
                .Select(MapValidationIssue)
                .ToArray());
    }

    private static GameConfigValidationIssueDto MapValidationIssue(GameConfigValidationIssue issue)
    {
        return new GameConfigValidationIssueDto(
            issue.Code,
            issue.Location,
            issue.Message,
            issue.LineNumber.HasValue ? checked((int)issue.LineNumber.Value) : null,
            issue.BytePositionInLine.HasValue ? checked((int)issue.BytePositionInLine.Value) : null);
    }

    private static GameConfigVersionSummaryDto MapVersionSummary(PublishedGameConfigVersion version)
    {
        return new GameConfigVersionSummaryDto(
            version.Id,
            version.ConfigurationSetKey,
            version.SchemaVersion,
            version.SourceDraftId,
            version.SourceDraftRevision,
            version.ReleaseNotes,
            version.ContentHash,
            version.SnapshotHash,
            Encoding.UTF8.GetByteCount(version.SnapshotJson),
            version.PublishedAtUtc,
            version.PublishedByAdministratorId);
    }

    private static GameConfigVersionDto MapVersion(PublishedGameConfigVersion version)
    {
        return new GameConfigVersionDto(
            version.Id,
            version.ConfigurationSetKey,
            version.SchemaVersion,
            version.SourceDraftId,
            version.SourceDraftRevision,
            version.ReleaseNotes,
            version.ContentHash,
            version.SnapshotHash,
            Encoding.UTF8.GetByteCount(version.SnapshotJson),
            version.CanonicalContentJson,
            version.PublishedAtUtc,
            version.PublishedByAdministratorId);
    }

    private static GameConfigEnvironmentDto MapEnvironment(
        GameConfigEnvironment environment,
        PublishedGameConfigVersion activeVersion)
    {
        return new GameConfigEnvironmentDto(
            environment.ConfigurationSetKey,
            environment.EnvironmentName,
            environment.Revision,
            environment.ActiveVersionId,
            environment.PreviousVersionId,
            activeVersion.SchemaVersion,
            activeVersion.ContentHash,
            environment.ActivatedAtUtc,
            environment.ActivatedByAdministratorId,
            environment.Reason);
    }

    private static GameConfigDraftState GetDraftState(GameConfigDraft draft)
    {
        if (draft.IsPublished)
        {
            return GameConfigDraftState.Published;
        }

        return draft.ValidationStatus switch
        {
            GameConfigValidationStatus.Valid when draft.LastValidatedRevision == draft.Revision =>
                GameConfigDraftState.Validated,
            GameConfigValidationStatus.Invalid when draft.LastValidatedRevision == draft.Revision =>
                GameConfigDraftState.ValidationFailed,
            _ => GameConfigDraftState.Editing
        };
    }

    private static IReadOnlyDictionary<string, string[]> ToValidationDetails(
        IEnumerable<GameConfigValidationIssue> issues)
    {
        return issues
            .GroupBy(issue => issue.Location, StringComparer.Ordinal)
            .ToDictionary(
                group => string.IsNullOrEmpty(group.Key) ? "$" : group.Key,
                group => group.Select(issue => $"{issue.Code}: {issue.Message}").ToArray(),
                StringComparer.Ordinal);
    }

    private static GameConfigSetKey ParseConfigurationSet(string value)
    {
        try
        {
            return GameConfigSetKey.Create(value);
        }
        catch (ArgumentException exception)
        {
            throw InvalidRequest(exception.Message);
        }
    }

    private static GameConfigEnvironmentName ParseEnvironmentName(string value)
    {
        try
        {
            return GameConfigEnvironmentName.Create(value);
        }
        catch (ArgumentException exception)
        {
            throw InvalidRequest(exception.Message);
        }
    }

    private static void EnsureConfigurationSetMatches(
        GameConfigSetKey configurationSet,
        PublishedGameConfigVersion version)
    {
        if (!string.Equals(
                configurationSet.Value,
                version.ConfigurationSetKey,
                StringComparison.Ordinal))
        {
            throw new GameConfigException(
                GameConfigFailureKind.UnprocessableEntity,
                GameConfigErrorCodes.ConfigurationSetMismatch,
                "The published version belongs to a different configuration set.");
        }
    }

    private static void EnsureNotEmpty(Guid value, string message)
    {
        if (value == Guid.Empty)
        {
            throw InvalidRequest(message);
        }
    }

    private DateTime GetNowUtc()
    {
        var value = _clock.UtcNow;
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new InvalidOperationException("The configured clock must return UTC timestamps.");
        }

        return new DateTime(value.Ticks - (value.Ticks % 10), DateTimeKind.Utc);
    }

    private static int GetSkipCount(int page, int pageSize)
    {
        if (page <= 0 || pageSize <= 0 || pageSize > GameConfigContractLimits.MaximumPageSize)
        {
            throw InvalidRequest("The requested page is invalid.");
        }

        var skipCount = ((long)page - 1) * pageSize;
        if (skipCount > int.MaxValue)
        {
            throw InvalidRequest("The requested page is outside the supported range.");
        }

        return (int)skipCount;
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static void TranslateDomain(Action action)
    {
        try
        {
            action();
        }
        catch (GameConfigDomainException exception)
        {
            throw TranslateDomainException(exception);
        }
        catch (ArgumentException exception)
        {
            throw InvalidRequest(exception.Message);
        }
    }

    private static T TranslateDomain<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (GameConfigDomainException exception)
        {
            throw TranslateDomainException(exception);
        }
        catch (ArgumentException exception)
        {
            throw InvalidRequest(exception.Message);
        }
    }

    private static GameConfigException TranslateDomainException(GameConfigDomainException exception)
    {
        var failureKind = exception.Code switch
        {
            GameConfigErrorCodes.DraftRevisionConflict or
            GameConfigErrorCodes.DraftRevisionAlreadyPublished or
            GameConfigErrorCodes.ActivationConflict => GameConfigFailureKind.Conflict,
            GameConfigErrorCodes.DraftNotValidated => GameConfigFailureKind.UnprocessableEntity,
            _ => GameConfigFailureKind.BadRequest
        };
        return new GameConfigException(failureKind, exception.Code, exception.Message);
    }

    private static GameConfigException RevisionConflict(long expectedRevision, long currentRevision)
    {
        return new GameConfigException(
            GameConfigFailureKind.Conflict,
            GameConfigErrorCodes.DraftRevisionConflict,
            $"Draft revision {expectedRevision} is stale; the current revision is {currentRevision}.");
    }

    private static GameConfigException ActivationConflict(long expectedRevision, long currentRevision)
    {
        return new GameConfigException(
            GameConfigFailureKind.Conflict,
            GameConfigErrorCodes.ActivationConflict,
            $"Environment revision {expectedRevision} is stale; the current revision is {currentRevision}.");
    }

    private static GameConfigException InvalidRequest(string message)
    {
        return new GameConfigException(
            GameConfigFailureKind.BadRequest,
            GameConfigErrorCodes.InvalidRequest,
            message);
    }
}
