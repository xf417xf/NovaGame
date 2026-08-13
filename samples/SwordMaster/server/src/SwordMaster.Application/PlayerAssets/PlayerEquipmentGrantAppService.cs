using NovaCore.BuildingBlocks.Infrastructure;
using NovaCore.BuildingBlocks.Permissions;
using SwordMaster.Application.Contracts.PlayerAssets;
using SwordMaster.Domain.PlayerAssets;
using ContractStatus = SwordMaster.Application.Contracts.PlayerAssets.PlayerEquipmentGrantStatusDto;
using DomainErrorCodes = SwordMaster.Domain.PlayerAssets.PlayerAssetsErrorCodes;
using DomainStatus = SwordMaster.Domain.PlayerAssets.PlayerEquipmentGrantStatus;

namespace SwordMaster.Application.PlayerAssets;

public sealed class PlayerEquipmentGrantAppService : IPlayerEquipmentGrantAppService
{
    private const int MaximumPageSize = 100;

    private readonly ICurrentSwordMasterAdministrator _currentAdministrator;
    private readonly IPermissionChecker _permissionChecker;
    private readonly IPlayerEquipmentGrantAuditContextAccessor _auditContextAccessor;
    private readonly IEquipmentDefinitionProvider _equipmentDefinitionProvider;
    private readonly IPlayerAssetsProfileRepository _profileRepository;
    private readonly IPlayerEquipmentGrantRepository _grantRepository;
    private readonly IPlayerAssetsTransactionManager _transactionManager;
    private readonly IGuidGenerator _guidGenerator;
    private readonly TimeProvider _timeProvider;
    private readonly PlayerEquipmentGrantRequestFingerprint _requestFingerprint;

    public PlayerEquipmentGrantAppService(
        ICurrentSwordMasterAdministrator currentAdministrator,
        IPermissionChecker permissionChecker,
        IPlayerEquipmentGrantAuditContextAccessor auditContextAccessor,
        IEquipmentDefinitionProvider equipmentDefinitionProvider,
        IPlayerAssetsProfileRepository profileRepository,
        IPlayerEquipmentGrantRepository grantRepository,
        IPlayerAssetsTransactionManager transactionManager,
        IGuidGenerator guidGenerator,
        TimeProvider timeProvider,
        PlayerEquipmentGrantRequestFingerprint requestFingerprint)
    {
        _currentAdministrator = currentAdministrator;
        _permissionChecker = permissionChecker;
        _auditContextAccessor = auditContextAccessor;
        _equipmentDefinitionProvider = equipmentDefinitionProvider;
        _profileRepository = profileRepository;
        _grantRepository = grantRepository;
        _transactionManager = transactionManager;
        _guidGenerator = guidGenerator;
        _timeProvider = timeProvider;
        _requestFingerprint = requestFingerprint;
    }

    public async Task<PlayerEquipmentGrantPageDto> GetPageAsync(
        Guid playerId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var parsedPlayerId = ParsePlayerId(playerId);
        var skipCount = GetSkipCount(page, pageSize);
        await RequireAdministratorAsync(cancellationToken);

        var profile = await _profileRepository.FindByPlayerIdAsync(
            parsedPlayerId,
            cancellationToken);
        if (profile is null)
        {
            throw PlayerNotFound();
        }

        var grants = await _grantRepository.GetListAsync(
            parsedPlayerId,
            skipCount,
            pageSize,
            cancellationToken);
        var totalCount = await _grantRepository.GetCountAsync(
            parsedPlayerId,
            cancellationToken);
        var items = grants
            .OrderByDescending(grant => grant.GrantedAtUtc)
            .ThenByDescending(grant => grant.GrantId)
            .Select(MapGrant)
            .ToArray();

        return new PlayerEquipmentGrantPageDto(
            playerId,
            profile.Revision,
            page,
            pageSize,
            totalCount,
            items);
    }

    public async Task<PlayerEquipmentGrantMutationResultDto> GrantAsync(
        Guid playerId,
        GrantPlayerEquipmentRequest request,
        PlayerEquipmentGrantMutationContext context,
        CancellationToken cancellationToken = default)
    {
        var parsedPlayerId = ParsePlayerId(playerId);
        EnsureRequest(request);
        EnsureMutationContext(context);
        var configuredItemId = NormalizeConfiguredItemId(request.ConfiguredItemId);
        var reason = NormalizeReason(request.Reason);
        var administratorId = await RequireAdministratorAsync(cancellationToken);
        var fingerprint = _requestFingerprint.CreateGrant(
            playerId,
            context.ExpectedRevision,
            configuredItemId,
            reason);

        var replay = await FindGrantReplayAsync(
            parsedPlayerId,
            context.IdempotencyKey,
            fingerprint,
            cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var definition = await ResolveDefinitionAsync(
            configuredItemId,
            cancellationToken);

        await using var transaction = await _transactionManager.BeginAsync(cancellationToken);
        var profile = await _profileRepository.FindByPlayerIdForUpdateAsync(
            parsedPlayerId,
            cancellationToken);
        if (profile is null)
        {
            throw PlayerNotFound();
        }

        replay = await FindGrantReplayAsync(
            parsedPlayerId,
            context.IdempotencyKey,
            fingerprint,
            cancellationToken);
        if (replay is not null)
        {
            await transaction.CompleteAsync(cancellationToken);
            return replay;
        }

        EnsureExpectedRevision(profile, context.ExpectedRevision);
        var grantId = _guidGenerator.Create();
        var generatedOwnedItemId = _guidGenerator.Create();
        EnsureGeneratedId(grantId);
        EnsureGeneratedId(generatedOwnedItemId);
        var ownedItemId = OwnedItemId.From(generatedOwnedItemId);
        TranslateDomain(() => profile.GrantEquipment(
            context.ExpectedRevision,
            ownedItemId,
            definition.ConfiguredItemId));

        var auditContext = _auditContextAccessor.Current;
        var grant = new PlayerEquipmentGrant(
            grantId,
            parsedPlayerId,
            ownedItemId,
            definition.ConfiguredItemId,
            definition.ConfigurationVersionId,
            administratorId,
            GetNowUtc(),
            reason,
            context.IdempotencyKey,
            fingerprint,
            profile.Revision,
            auditContext.IpAddress,
            auditContext.UserAgent,
            auditContext.CorrelationId);
        await _grantRepository.AddAsync(grant, cancellationToken);
        await transaction.CompleteAsync(cancellationToken);
        return MapGrantResult(grant);
    }

    public async Task<PlayerEquipmentGrantMutationResultDto> RollbackAsync(
        Guid playerId,
        Guid grantId,
        RollbackPlayerEquipmentGrantRequest request,
        PlayerEquipmentGrantMutationContext context,
        CancellationToken cancellationToken = default)
    {
        var parsedPlayerId = ParsePlayerId(playerId);
        EnsureGrantId(grantId);
        EnsureRequest(request);
        EnsureMutationContext(context);
        var reason = NormalizeReason(request.Reason);
        var administratorId = await RequireAdministratorAsync(cancellationToken);
        var fingerprint = _requestFingerprint.CreateRollback(
            playerId,
            grantId,
            context.ExpectedRevision,
            reason);

        var replay = await FindRollbackReplayAsync(
            parsedPlayerId,
            context.IdempotencyKey,
            fingerprint,
            cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        await using var transaction = await _transactionManager.BeginAsync(cancellationToken);
        var profile = await _profileRepository.FindByPlayerIdForUpdateAsync(
            parsedPlayerId,
            cancellationToken);
        if (profile is null)
        {
            throw PlayerNotFound();
        }

        var grant = await _grantRepository.FindByIdForUpdateAsync(
            parsedPlayerId,
            grantId,
            cancellationToken);
        replay = await FindRollbackReplayAsync(
            parsedPlayerId,
            context.IdempotencyKey,
            fingerprint,
            cancellationToken);
        if (replay is not null)
        {
            await transaction.CompleteAsync(cancellationToken);
            return replay;
        }

        if (grant is null)
        {
            throw GrantNotFound();
        }

        if (grant.Status != DomainStatus.Active)
        {
            throw new PlayerEquipmentGrantException(
                PlayerEquipmentGrantFailureKind.Conflict,
                PlayerEquipmentGrantErrorCodes.AlreadyRolledBack,
                "The equipment grant has already been rolled back.");
        }

        EnsureExpectedRevision(profile, context.ExpectedRevision);
        TranslateDomain(() => profile.RollbackGrantedEquipment(
            context.ExpectedRevision,
            grant.OwnedItemId));
        var auditContext = _auditContextAccessor.Current;
        TranslateDomain(() => grant.Rollback(
            administratorId,
            GetNowUtc(),
            reason,
            context.IdempotencyKey,
            fingerprint,
            profile.Revision,
            auditContext.IpAddress,
            auditContext.UserAgent,
            auditContext.CorrelationId));

        await transaction.CompleteAsync(cancellationToken);
        return MapRollbackResult(grant);
    }

    private async Task<Guid> RequireAdministratorAsync(
        CancellationToken cancellationToken)
    {
        if (!_currentAdministrator.IsAuthenticated ||
            !_currentAdministrator.AdministratorId.HasValue ||
            _currentAdministrator.AdministratorId.Value == Guid.Empty)
        {
            throw new PlayerEquipmentGrantException(
                PlayerEquipmentGrantFailureKind.Unauthorized,
                PlayerEquipmentGrantErrorCodes.UnauthorizedAdministrator,
                "An authenticated NovaGame administrator is required.");
        }

        if (!await _permissionChecker.IsGrantedAsync(
                PlayerEquipmentGrantPermissions.Manage,
                cancellationToken))
        {
            throw new PlayerEquipmentGrantException(
                PlayerEquipmentGrantFailureKind.Forbidden,
                PlayerEquipmentGrantErrorCodes.Forbidden,
                "The current administrator cannot manage player equipment grants.");
        }

        return _currentAdministrator.AdministratorId.Value;
    }

    private async Task<EquipmentDefinition> ResolveDefinitionAsync(
        string configuredItemId,
        CancellationToken cancellationToken)
    {
        var resolution = await _equipmentDefinitionProvider.ResolveActiveAsync(
            configuredItemId,
            cancellationToken);
        return resolution.Status switch
        {
            EquipmentDefinitionResolutionStatus.Found
                when resolution.Definition is not null &&
                    string.Equals(
                        resolution.Definition.ConfiguredItemId,
                        configuredItemId,
                        StringComparison.Ordinal) => resolution.Definition,
            EquipmentDefinitionResolutionStatus.Missing =>
                throw new PlayerEquipmentGrantException(
                    PlayerEquipmentGrantFailureKind.UnprocessableEntity,
                    PlayerEquipmentGrantErrorCodes.ConfigurationNotFound,
                    "The active configuration does not contain the requested equipment definition."),
            EquipmentDefinitionResolutionStatus.Invalid =>
                throw ConfigurationInvalid(),
            _ => throw ConfigurationInvalid()
        };
    }

    private async Task<PlayerEquipmentGrantMutationResultDto?> FindGrantReplayAsync(
        PlayerId playerId,
        Guid idempotencyKey,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        var grant = await _grantRepository.FindByGrantIdempotencyKeyAsync(
            playerId,
            idempotencyKey,
            cancellationToken);
        if (grant is null)
        {
            return null;
        }

        EnsureFingerprintMatches(grant.MatchesGrantRequest(fingerprint));
        return MapGrantResult(grant);
    }

    private async Task<PlayerEquipmentGrantMutationResultDto?> FindRollbackReplayAsync(
        PlayerId playerId,
        Guid idempotencyKey,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        var grant = await _grantRepository.FindByRollbackIdempotencyKeyAsync(
            playerId,
            idempotencyKey,
            cancellationToken);
        if (grant is null)
        {
            return null;
        }

        EnsureFingerprintMatches(grant.MatchesRollbackRequest(fingerprint));
        return MapRollbackResult(grant);
    }

    private static void EnsureFingerprintMatches(bool matches)
    {
        if (!matches)
        {
            throw new PlayerEquipmentGrantException(
                PlayerEquipmentGrantFailureKind.Conflict,
                PlayerEquipmentGrantErrorCodes.IdempotencyConflict,
                "The idempotency key was already used for another request in this command scope.");
        }
    }

    private static PlayerEquipmentGrantDto MapGrant(PlayerEquipmentGrant grant)
    {
        return new PlayerEquipmentGrantDto(
            grant.GrantId,
            grant.PlayerId.Value,
            grant.OwnedItemId.Value,
            grant.ConfiguredItemId,
            grant.ConfigurationVersionId,
            MapStatus(grant.Status),
            grant.GrantResultingRevision,
            grant.GrantedByAdministratorId,
            grant.GrantedAtUtc,
            grant.GrantReason,
            grant.RollbackResultingRevision,
            grant.RolledBackByAdministratorId,
            grant.RolledBackAtUtc,
            grant.RollbackReason);
    }

    private static PlayerEquipmentGrantMutationResultDto MapGrantResult(
        PlayerEquipmentGrant grant)
    {
        return new PlayerEquipmentGrantMutationResultDto(
            grant.GrantId,
            grant.PlayerId.Value,
            grant.OwnedItemId.Value,
            grant.ConfiguredItemId,
            grant.ConfigurationVersionId,
            ContractStatus.Active,
            grant.GrantResultingRevision,
            grant.GrantedByAdministratorId,
            grant.GrantedAtUtc,
            grant.GrantReason,
            RolledBackByAdministratorId: null,
            RolledBackAtUtc: null,
            RollbackReason: null);
    }

    private static PlayerEquipmentGrantMutationResultDto MapRollbackResult(
        PlayerEquipmentGrant grant)
    {
        if (grant.Status != DomainStatus.RolledBack ||
            !grant.RollbackResultingRevision.HasValue ||
            !grant.RolledBackByAdministratorId.HasValue ||
            !grant.RolledBackAtUtc.HasValue ||
            grant.RollbackReason is null)
        {
            throw new InvalidOperationException(
                "A rollback receipt must contain one complete rollback result.");
        }

        return new PlayerEquipmentGrantMutationResultDto(
            grant.GrantId,
            grant.PlayerId.Value,
            grant.OwnedItemId.Value,
            grant.ConfiguredItemId,
            grant.ConfigurationVersionId,
            ContractStatus.RolledBack,
            grant.RollbackResultingRevision.Value,
            grant.GrantedByAdministratorId,
            grant.GrantedAtUtc,
            grant.GrantReason,
            grant.RolledBackByAdministratorId,
            grant.RolledBackAtUtc,
            grant.RollbackReason);
    }

    private static ContractStatus MapStatus(DomainStatus status)
    {
        return status switch
        {
            DomainStatus.Active => ContractStatus.Active,
            DomainStatus.RolledBack => ContractStatus.RolledBack,
            _ => throw new InvalidOperationException(
                "The stored equipment-grant status is invalid.")
        };
    }

    private static PlayerId ParsePlayerId(Guid playerId)
    {
        try
        {
            return PlayerId.From(playerId);
        }
        catch (ArgumentException exception)
        {
            throw InvalidRequest(exception.Message);
        }
    }

    private static void EnsureGrantId(Guid grantId)
    {
        if (grantId == Guid.Empty)
        {
            throw InvalidRequest("A grant identifier is required.");
        }
    }

    private static void EnsureGeneratedId(Guid generatedId)
    {
        if (generatedId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "The configured GUID generator returned an empty identifier.");
        }
    }

    private static int GetSkipCount(int page, int pageSize)
    {
        if (page <= 0)
        {
            throw InvalidRequest("Page must be positive.");
        }

        if (pageSize is < 1 or > MaximumPageSize)
        {
            throw InvalidRequest($"Page size must be from 1 through {MaximumPageSize}.");
        }

        var skipCount = ((long)page - 1) * pageSize;
        if (skipCount > int.MaxValue)
        {
            throw InvalidRequest("The requested page is outside the supported range.");
        }

        return (int)skipCount;
    }

    private static void EnsureRequest(GrantPlayerEquipmentRequest? request)
    {
        if (request is null)
        {
            throw InvalidRequest("A grant request body is required.");
        }
    }

    private static void EnsureRequest(RollbackPlayerEquipmentGrantRequest? request)
    {
        if (request is null)
        {
            throw InvalidRequest("A rollback request body is required.");
        }
    }

    private static void EnsureMutationContext(
        PlayerEquipmentGrantMutationContext? context)
    {
        if (context is null)
        {
            throw InvalidRequest("Mutation headers are required.");
        }

        if (context.ExpectedRevision <= 0)
        {
            throw InvalidRequest("Expected revision must be positive.");
        }

        if (context.IdempotencyKey == Guid.Empty)
        {
            throw InvalidRequest("An idempotency key is required.");
        }
    }

    private static string NormalizeConfiguredItemId(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized) ||
            normalized.Length > EquipmentDefinition.ConfiguredItemIdMaximumLength ||
            normalized.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)))
        {
            throw InvalidRequest(
                $"Configured item identifiers must contain 1 to " +
                $"{EquipmentDefinition.ConfiguredItemIdMaximumLength} non-whitespace, non-control characters.");
        }

        return normalized;
    }

    private static string NormalizeReason(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized) ||
            normalized.Length > PlayerEquipmentGrant.ReasonMaximumLength ||
            normalized.Any(char.IsControl))
        {
            throw InvalidRequest(
                $"Reasons must contain 1 to {PlayerEquipmentGrant.ReasonMaximumLength} non-control characters.");
        }

        return normalized;
    }

    private static void EnsureExpectedRevision(
        PlayerAssetsProfile profile,
        long expectedRevision)
    {
        if (profile.Revision != expectedRevision)
        {
            throw new PlayerEquipmentGrantException(
                PlayerEquipmentGrantFailureKind.Conflict,
                PlayerEquipmentGrantErrorCodes.ConcurrencyConflict,
                $"Player-assets revision {expectedRevision} is stale; " +
                $"the current revision is {profile.Revision}.");
        }
    }

    private DateTime GetNowUtc()
    {
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        return new DateTime(
            utcNow.Ticks - (utcNow.Ticks % TimeSpan.TicksPerMicrosecond),
            DateTimeKind.Utc);
    }

    private static T TranslateDomain<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (PlayerAssetsDomainException exception)
        {
            throw TranslateDomainException(exception);
        }
    }

    private static void TranslateDomain(Action action)
    {
        try
        {
            action();
        }
        catch (PlayerAssetsDomainException exception)
        {
            throw TranslateDomainException(exception);
        }
    }

    private static Exception TranslateDomainException(
        PlayerAssetsDomainException exception)
    {
        var translation = exception.Code switch
        {
            DomainErrorCodes.ConcurrencyConflict => (
                PlayerEquipmentGrantFailureKind.Conflict,
                PlayerEquipmentGrantErrorCodes.ConcurrencyConflict),
            DomainErrorCodes.EquipmentGrantAlreadyRolledBack => (
                PlayerEquipmentGrantFailureKind.Conflict,
                PlayerEquipmentGrantErrorCodes.AlreadyRolledBack),
            DomainErrorCodes.EquipmentGrantRollbackBlockedItemEquipped => (
                PlayerEquipmentGrantFailureKind.Conflict,
                PlayerEquipmentGrantErrorCodes.RollbackBlockedItemEquipped),
            DomainErrorCodes.EquipmentGrantGrantedItemMissing => (
                PlayerEquipmentGrantFailureKind.Conflict,
                PlayerEquipmentGrantErrorCodes.GrantedItemMissing),
            _ => ((PlayerEquipmentGrantFailureKind FailureKind, string Code)?)null
        };

        if (!translation.HasValue)
        {
            return new InvalidOperationException(
                "An unexpected PlayerAssets domain failure occurred.",
                exception);
        }

        return new PlayerEquipmentGrantException(
            translation.Value.FailureKind,
            translation.Value.Code,
            exception.Message);
    }

    private static PlayerEquipmentGrantException InvalidRequest(string message)
    {
        return new PlayerEquipmentGrantException(
            PlayerEquipmentGrantFailureKind.BadRequest,
            PlayerEquipmentGrantErrorCodes.InvalidRequest,
            message);
    }

    private static PlayerEquipmentGrantException PlayerNotFound()
    {
        return new PlayerEquipmentGrantException(
            PlayerEquipmentGrantFailureKind.NotFound,
            PlayerEquipmentGrantErrorCodes.PlayerNotFound,
            "The requested player-assets profile was not found.");
    }

    private static PlayerEquipmentGrantException GrantNotFound()
    {
        return new PlayerEquipmentGrantException(
            PlayerEquipmentGrantFailureKind.NotFound,
            PlayerEquipmentGrantErrorCodes.GrantNotFound,
            "The requested equipment grant was not found for this player.");
    }

    private static PlayerEquipmentGrantException ConfigurationInvalid()
    {
        return new PlayerEquipmentGrantException(
            PlayerEquipmentGrantFailureKind.UnprocessableEntity,
            PlayerEquipmentGrantErrorCodes.ConfigurationInvalid,
            "The active equipment configuration is invalid.");
    }
}
