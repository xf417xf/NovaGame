using System.Security.Cryptography;
using NovaCore.BuildingBlocks.CurrentUser;
using NovaCore.BuildingBlocks.Infrastructure;
using NovaCore.BuildingBlocks.Timing;
using NovaCore.BuildingBlocks.UnitOfWork;
using NovaGame.Application.Auditing;
using NovaGame.Application.Contracts.Operations.AdminAccess;
using NovaGame.Domain.Operations.AdminAccess;
using NovaGame.Domain.Shared.Operations.AdminAccess;

namespace NovaGame.Application.Operations.AdminAccess;

public sealed class AdminAccessAppService : IAdminAccessAppService
{
    private static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(7);

    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IAdministratorRepository _administratorRepository;
    private readonly IRefreshTokenSessionRepository _refreshTokenSessionRepository;
    private readonly IAdminAccessAuditRepository _auditRepository;
    private readonly IPasswordHashingService _passwordHashingService;
    private readonly IAccessTokenIssuer _accessTokenIssuer;
    private readonly IRefreshTokenService _refreshTokenService;
    private readonly RefreshTokenFamilyPolicy _refreshTokenFamilyPolicy;
    private readonly IRequestAuditContextAccessor _requestAuditContextAccessor;
    private readonly ICurrentUser _currentUser;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IClock _clock;

    public AdminAccessAppService(
        IUnitOfWorkManager unitOfWorkManager,
        IAdministratorRepository administratorRepository,
        IRefreshTokenSessionRepository refreshTokenSessionRepository,
        IAdminAccessAuditRepository auditRepository,
        IPasswordHashingService passwordHashingService,
        IAccessTokenIssuer accessTokenIssuer,
        IRefreshTokenService refreshTokenService,
        RefreshTokenFamilyPolicy refreshTokenFamilyPolicy,
        IRequestAuditContextAccessor requestAuditContextAccessor,
        ICurrentUser currentUser,
        IGuidGenerator guidGenerator,
        IClock clock)
    {
        _unitOfWorkManager = unitOfWorkManager;
        _administratorRepository = administratorRepository;
        _refreshTokenSessionRepository = refreshTokenSessionRepository;
        _auditRepository = auditRepository;
        _passwordHashingService = passwordHashingService;
        _accessTokenIssuer = accessTokenIssuer;
        _refreshTokenService = refreshTokenService;
        _refreshTokenFamilyPolicy = refreshTokenFamilyPolicy;
        _requestAuditContextAccessor = requestAuditContextAccessor;
        _currentUser = currentUser;
        _guidGenerator = guidGenerator;
        _clock = clock;
    }

    public async Task<AuthenticationSession> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var unitOfWork = await _unitOfWorkManager.BeginAsync(cancellationToken: cancellationToken);
        var now = _clock.UtcNow;
        Administrator? administrator = null;

        if (AdministratorUserName.TryCreate(request.UserName, out var userName))
        {
            administrator = await _administratorRepository.FindByNormalizedUserNameForUpdateAsync(
                userName.NormalizedValue,
                cancellationToken);
        }

        var passwordResult = _passwordHashingService.Verify(administrator?.PasswordHash, request.Password);
        if (administrator is null || passwordResult == PasswordHashVerificationResult.Failed)
        {
            await AddAuditAsync(
                AdminAccessAuditAction.Login,
                now,
                succeeded: false,
                administratorId: administrator?.Id,
                errorCode: AdminAccessErrorCodes.InvalidCredentials,
                cancellationToken: cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);
            throw InvalidCredentials();
        }

        if (!administrator.IsActive)
        {
            await AddAuditAsync(
                AdminAccessAuditAction.Login,
                now,
                succeeded: false,
                administratorId: administrator.Id,
                errorCode: AdminAccessErrorCodes.AccountInactive,
                cancellationToken: cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);
            throw InvalidCredentials();
        }

        if (passwordResult == PasswordHashVerificationResult.SuccessRehashNeeded)
        {
            administrator.SetPasswordHash(_passwordHashingService.Hash(request.Password), now);
            await AddAuditAsync(
                AdminAccessAuditAction.PasswordRehashed,
                now,
                succeeded: true,
                administratorId: administrator.Id,
                cancellationToken: cancellationToken);
        }

        administrator.RecordSuccessfulLogin(now);
        var refreshToken = _refreshTokenService.Create();
        var refreshTokenExpiresAtUtc = now.Add(RefreshTokenLifetime);
        var refreshSession = new RefreshTokenSession(
            _guidGenerator.Create(),
            administrator.Id,
            _guidGenerator.Create(),
            refreshToken.Hash,
            now,
            refreshTokenExpiresAtUtc,
            _requestAuditContextAccessor.Current.IpAddress);
        await _refreshTokenSessionRepository.AddAsync(refreshSession, cancellationToken);

        var accessTokenExpiresAtUtc = now.Add(AccessTokenLifetime);
        var accessToken = _accessTokenIssuer.Issue(
            administrator.Id,
            administrator.UserName,
            now,
            accessTokenExpiresAtUtc);

        await AddAuditAsync(
            AdminAccessAuditAction.Login,
            now,
            succeeded: true,
            administratorId: administrator.Id,
            cancellationToken: cancellationToken);
        await unitOfWork.CompleteAsync(cancellationToken);

        return new AuthenticationSession(
            accessToken,
            accessTokenExpiresAtUtc,
            refreshToken.Value,
            refreshTokenExpiresAtUtc);
    }

    public async Task<AuthenticationSession> RefreshAsync(
        string? refreshToken,
        CancellationToken cancellationToken = default)
    {
        if (!_refreshTokenService.TryHash(refreshToken, out var tokenHash))
        {
            await using var invalidTokenUnitOfWork = await _unitOfWorkManager.BeginAsync(
                cancellationToken: cancellationToken);
            var now = _clock.UtcNow;
            await AddAuditAsync(
                AdminAccessAuditAction.Refresh,
                now,
                succeeded: false,
                errorCode: string.IsNullOrWhiteSpace(refreshToken)
                    ? AdminAccessErrorCodes.RefreshTokenRequired
                    : AdminAccessErrorCodes.RefreshTokenInvalid,
                cancellationToken: cancellationToken);
            await invalidTokenUnitOfWork.CompleteAsync(cancellationToken);
            throw new AdminAccessException(
                string.IsNullOrWhiteSpace(refreshToken)
                    ? AdminAccessErrorCodes.RefreshTokenRequired
                    : AdminAccessErrorCodes.RefreshTokenInvalid,
                "A valid refresh token is required.");
        }

        var tokenProbe = await _refreshTokenSessionRepository.FindByTokenHashAsync(
            tokenHash,
            cancellationToken);
        if (tokenProbe is null)
        {
            await using var missingTokenUnitOfWork = await _unitOfWorkManager.BeginAsync(
                cancellationToken: cancellationToken);
            var now = _clock.UtcNow;
            await AddAuditAsync(
                AdminAccessAuditAction.Refresh,
                now,
                succeeded: false,
                errorCode: AdminAccessErrorCodes.RefreshTokenInvalid,
                cancellationToken: cancellationToken);
            await missingTokenUnitOfWork.CompleteAsync(cancellationToken);
            throw new AdminAccessException(
                AdminAccessErrorCodes.RefreshTokenInvalid,
                "The refresh token is invalid.");
        }

        await using var unitOfWork = await _unitOfWorkManager.BeginAsync(cancellationToken: cancellationToken);
        var administrator = await _administratorRepository.FindByIdForUpdateAsync(
            tokenProbe.AdministratorId,
            cancellationToken);
        if (administrator is null)
        {
            var missingAdministratorTime = _clock.UtcNow;
            await AddAuditAsync(
                AdminAccessAuditAction.Refresh,
                missingAdministratorTime,
                succeeded: false,
                administratorId: tokenProbe.AdministratorId,
                errorCode: AdminAccessErrorCodes.RefreshTokenInvalid,
                cancellationToken: cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);
            throw new AdminAccessException(
                AdminAccessErrorCodes.RefreshTokenInvalid,
                "The refresh token is invalid.");
        }

        var sessions = await _refreshTokenSessionRepository.GetFamilyForUpdateAsync(
            tokenProbe.FamilyId,
            cancellationToken);
        var currentTime = _clock.UtcNow;
        var identity = _refreshTokenFamilyPolicy.RequireValidIdentity(sessions);
        var session = sessions.SingleOrDefault(candidate =>
            CryptographicOperations.FixedTimeEquals(candidate.TokenHash, tokenHash));
        if (session is null ||
            identity.FamilyId != tokenProbe.FamilyId ||
            identity.AdministratorId != tokenProbe.AdministratorId ||
            session.FamilyId != tokenProbe.FamilyId ||
            session.AdministratorId != administrator.Id)
        {
            await AddAuditAsync(
                AdminAccessAuditAction.Refresh,
                currentTime,
                succeeded: false,
                administratorId: administrator.Id,
                errorCode: AdminAccessErrorCodes.RefreshTokenInvalid,
                cancellationToken: cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);
            throw new AdminAccessException(
                AdminAccessErrorCodes.RefreshTokenInvalid,
                "The refresh token is invalid.");
        }

        if (session.IsRevoked)
        {
            if (session.WasRotated)
            {
                _ = _refreshTokenFamilyPolicy.Revoke(
                    sessions,
                    RefreshTokenRevocationReason.ReuseDetected,
                    currentTime,
                    _requestAuditContextAccessor.Current.IpAddress);
                await AddAuditAsync(
                    AdminAccessAuditAction.RefreshTokenReuseDetected,
                    currentTime,
                    succeeded: false,
                    administratorId: administrator.Id,
                    errorCode: AdminAccessErrorCodes.RefreshTokenReused,
                    cancellationToken: cancellationToken);
                await unitOfWork.CompleteAsync(cancellationToken);
                throw new AdminAccessException(
                    AdminAccessErrorCodes.RefreshTokenReused,
                    "Refresh-token reuse was detected and the token family was revoked.");
            }

            await AddAuditAsync(
                AdminAccessAuditAction.Refresh,
                currentTime,
                succeeded: false,
                administratorId: administrator.Id,
                errorCode: AdminAccessErrorCodes.RefreshTokenInvalid,
                cancellationToken: cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);
            throw new AdminAccessException(
                AdminAccessErrorCodes.RefreshTokenInvalid,
                "The refresh token is invalid.");
        }

        if (session.IsExpired(currentTime))
        {
            session.Revoke(
                RefreshTokenRevocationReason.Expired,
                currentTime,
                _requestAuditContextAccessor.Current.IpAddress);
            await AddAuditAsync(
                AdminAccessAuditAction.Refresh,
                currentTime,
                succeeded: false,
                administratorId: administrator.Id,
                errorCode: AdminAccessErrorCodes.RefreshTokenExpired,
                cancellationToken: cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);
            throw new AdminAccessException(
                AdminAccessErrorCodes.RefreshTokenExpired,
                "The refresh token has expired.");
        }

        if (!administrator.IsActive)
        {
            _ = _refreshTokenFamilyPolicy.Revoke(
                sessions,
                RefreshTokenRevocationReason.AdministratorInactive,
                currentTime,
                _requestAuditContextAccessor.Current.IpAddress);
            await AddAuditAsync(
                AdminAccessAuditAction.Refresh,
                currentTime,
                succeeded: false,
                administratorId: administrator.Id,
                errorCode: AdminAccessErrorCodes.AccountInactive,
                cancellationToken: cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);
            throw new AdminAccessException(
                AdminAccessErrorCodes.AccountInactive,
                "The administrator account is inactive.");
        }

        var replacementToken = _refreshTokenService.Create();
        var replacementSessionId = _guidGenerator.Create();
        var replacementExpiresAtUtc = currentTime.Add(RefreshTokenLifetime);
        session.Rotate(replacementSessionId, currentTime, _requestAuditContextAccessor.Current.IpAddress);
        await _refreshTokenSessionRepository.AddAsync(
            new RefreshTokenSession(
                replacementSessionId,
                administrator.Id,
                session.FamilyId,
                replacementToken.Hash,
                currentTime,
                replacementExpiresAtUtc,
                _requestAuditContextAccessor.Current.IpAddress),
            cancellationToken);

        var accessTokenExpiresAtUtc = currentTime.Add(AccessTokenLifetime);
        var accessToken = _accessTokenIssuer.Issue(
            administrator.Id,
            administrator.UserName,
            currentTime,
            accessTokenExpiresAtUtc);
        await AddAuditAsync(
            AdminAccessAuditAction.Refresh,
            currentTime,
            succeeded: true,
            administratorId: administrator.Id,
            cancellationToken: cancellationToken);
        await unitOfWork.CompleteAsync(cancellationToken);

        return new AuthenticationSession(
            accessToken,
            accessTokenExpiresAtUtc,
            replacementToken.Value,
            replacementExpiresAtUtc);
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken = default)
    {
        if (!_refreshTokenService.TryHash(refreshToken, out var tokenHash))
        {
            return;
        }

        var tokenProbe = await _refreshTokenSessionRepository.FindByTokenHashAsync(
            tokenHash,
            cancellationToken);
        if (tokenProbe is null)
        {
            return;
        }

        await using var unitOfWork = await _unitOfWorkManager.BeginAsync(cancellationToken: cancellationToken);
        var administrator = await _administratorRepository.FindByIdForUpdateAsync(
            tokenProbe.AdministratorId,
            cancellationToken);
        if (administrator is null)
        {
            await unitOfWork.CompleteAsync(cancellationToken);
            return;
        }

        var sessions = await _refreshTokenSessionRepository.GetFamilyForUpdateAsync(
            tokenProbe.FamilyId,
            cancellationToken);
        var identity = _refreshTokenFamilyPolicy.RequireValidIdentity(sessions);
        var session = sessions.SingleOrDefault(candidate =>
            CryptographicOperations.FixedTimeEquals(candidate.TokenHash, tokenHash));
        if (session is null ||
            identity.FamilyId != tokenProbe.FamilyId ||
            identity.AdministratorId != tokenProbe.AdministratorId ||
            session.FamilyId != tokenProbe.FamilyId ||
            session.AdministratorId != administrator.Id)
        {
            await unitOfWork.CompleteAsync(cancellationToken);
            return;
        }

        var now = _clock.UtcNow;
        _ = _refreshTokenFamilyPolicy.Revoke(
            sessions,
            RefreshTokenRevocationReason.Logout,
            now,
            _requestAuditContextAccessor.Current.IpAddress);
        await AddAuditAsync(
            AdminAccessAuditAction.Logout,
            now,
            succeeded: true,
            administratorId: administrator.Id,
            cancellationToken: cancellationToken);
        await unitOfWork.CompleteAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetPermissionCodesAsync(
        CancellationToken cancellationToken = default)
    {
        var snapshot = await GetCurrentAuthorizationSnapshotAsync(cancellationToken);
        return snapshot.PermissionNames.Order(StringComparer.Ordinal).ToArray();
    }

    public async Task<AdministratorInfoDto> GetCurrentAdministratorAsync(
        CancellationToken cancellationToken = default)
    {
        var snapshot = await GetCurrentAuthorizationSnapshotAsync(cancellationToken);
        return new AdministratorInfoDto(
            snapshot.AdministratorId,
            snapshot.UserName,
            snapshot.DisplayName,
            snapshot.RoleNames.Order(StringComparer.Ordinal).ToArray(),
            snapshot.PermissionNames.Order(StringComparer.Ordinal).ToArray());
    }

    private async Task<AdministratorAuthorizationSnapshot> GetCurrentAuthorizationSnapshotAsync(
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated ||
            !AdministratorIdentity.TryGetId(_currentUser, out var administratorId))
        {
            throw Unauthorized();
        }

        var snapshot = await _administratorRepository.GetAuthorizationSnapshotAsync(
            administratorId,
            cancellationToken);
        if (snapshot is not { IsActive: true })
        {
            throw Unauthorized();
        }

        return snapshot;
    }

    private Task AddAuditAsync(
        AdminAccessAuditAction action,
        DateTime occurredAtUtc,
        bool succeeded,
        Guid? administratorId = null,
        string? errorCode = null,
        CancellationToken cancellationToken = default)
    {
        var clientContext = _requestAuditContextAccessor.Current;
        return _auditRepository.AddAsync(
            new AdminAccessAuditRecord(
                _guidGenerator.Create(),
                action,
                occurredAtUtc,
                succeeded,
                administratorId: administratorId,
                errorCode: errorCode,
                ipAddress: clientContext.IpAddress,
                userAgent: clientContext.UserAgent,
                correlationId: clientContext.CorrelationId),
            cancellationToken);
    }

    private static AdminAccessException InvalidCredentials()
    {
        return new AdminAccessException(
            AdminAccessErrorCodes.InvalidCredentials,
            "The user name or password is invalid.");
    }

    private static AdminAccessException Unauthorized()
    {
        return new AdminAccessException(
            AdminAccessErrorCodes.Unauthorized,
            "Authentication is required.");
    }
}
