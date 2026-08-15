using NovaCore.BuildingBlocks.CurrentUser;
using NovaCore.BuildingBlocks.Infrastructure;
using NovaCore.BuildingBlocks.Timing;
using NovaCore.BuildingBlocks.UnitOfWork;
using NovaGame.Application.Auditing;
using NovaGame.Application.Contracts.Operations.AdminAccess;
using NovaGame.Application.Operations.AdminAccess;
using Microsoft.EntityFrameworkCore;
using NovaGame.Domain.Operations.AdminAccess;
using NovaGame.Domain.Shared.Operations.AdminAccess;
using NovaGame.Host.EntityFrameworkCore.Operations.AdminAccess;
using Xunit;

namespace NovaGame.EntityFrameworkCore.Tests;

[Collection(MySqlDatabaseCollection.Name)]
public sealed class AdminAccessManagementSessionTests
{
    private readonly MySqlDatabaseFixture _fixture;

    public AdminAccessManagementSessionTests(MySqlDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SessionFamilyProjectionAndCanonicalLockReturnTheValidatedFamily()
    {
        var now = new DateTime(2026, 8, 15, 4, 0, 0, DateTimeKind.Utc);
        var administratorId = Guid.NewGuid();
        var familyId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        var replacementId = Guid.NewGuid();
        var root = new RefreshTokenSession(
            rootId,
            administratorId,
            familyId,
            CreateHash(1),
            now.AddMinutes(-30),
            now.AddDays(7),
            "203.0.113.10");
        root.Rotate(replacementId, now.AddMinutes(-5), "203.0.113.11");
        var replacement = new RefreshTokenSession(
            replacementId,
            administratorId,
            familyId,
            CreateHash(2),
            now.AddMinutes(-5),
            now.AddDays(7),
            "203.0.113.12");

        await using (var seedContext = _fixture.CreateDbContext())
        {
            await seedContext.Administrators.AddAsync(new Administrator(
                administratorId,
                AdministratorUserName.Create($"session-{Guid.NewGuid():N}"),
                "Session test administrator",
                "test-password-hash",
                now.AddHours(-1)));
            await seedContext.RefreshTokenSessions.AddRangeAsync(root, replacement);
            await seedContext.SaveChangesAsync();
        }

        await using var dbContext = _fixture.CreateDbContext();
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var repository = new AdminAccessManagementRepository(
            dbContext,
            new RefreshTokenFamilyPolicy());

        var page = await repository.GetSessionFamilyPageAsync(
            administratorId,
            AdminSessionFamilyStateSnapshot.Active,
            now,
            skipCount: 0,
            maxResultCount: 10);
        var snapshot = Assert.Single(page.Items);
        var lockedRows = await repository.GetSessionFamilyForUpdateAsync(familyId);

        Assert.Equal(1, page.TotalCount);
        Assert.Equal(familyId, snapshot.FamilyId);
        Assert.Equal(administratorId, snapshot.AdministratorId);
        Assert.Equal(AdminSessionFamilyStateSnapshot.Active, snapshot.State);
        Assert.Equal(now.AddMinutes(-5), snapshot.LastActivityAtUtc);
        Assert.Equal("203.0.113.11", snapshot.LastIpAddress);
        Assert.Equal(new[] { rootId, replacementId }, lockedRows.Select(session => session.Id));
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task ReceiptClaimsAreAtomicForEveryFrozenCommandKind()
    {
        var commandKinds = new[]
        {
            AdminAccessCommandKind.AdministratorCreate,
            AdminAccessCommandKind.AdminRoleCreate,
            AdminAccessCommandKind.AdministratorPasswordReset
        };

        foreach (var commandKind in commandKinds)
        {
            var actorId = Guid.NewGuid();
            var key = Guid.NewGuid();
            var targetId = Guid.NewGuid();
            var createdAtUtc = DateTime.UtcNow;
            var firstCandidate = new AdminAccessCommandReceipt(
                Guid.NewGuid(),
                actorId,
                commandKind,
                key,
                CreateHash((byte)commandKind),
                targetId,
                resultingRevision: 1,
                createdAtUtc);

            await using (var firstContext = _fixture.CreateDbContext())
            {
                await using var firstTransaction = await firstContext.Database.BeginTransactionAsync();
                var firstRepository = new AdminAccessCommandReceiptRepository(firstContext);
                var claimed = await firstRepository.ClaimAsync(firstCandidate);
                Assert.Equal(firstCandidate.Id, claimed.Id);
                await firstContext.SaveChangesAsync();
                await firstTransaction.CommitAsync();
            }

            var duplicateCandidate = new AdminAccessCommandReceipt(
                Guid.NewGuid(),
                actorId,
                commandKind,
                key,
                CreateHash((byte)commandKind),
                targetId,
                resultingRevision: 1,
                createdAtUtc.AddSeconds(1));
            await using (var secondContext = _fixture.CreateDbContext())
            {
                await using var secondTransaction = await secondContext.Database.BeginTransactionAsync();
                var secondRepository = new AdminAccessCommandReceiptRepository(secondContext);
                var stored = await secondRepository.ClaimAsync(duplicateCandidate);

                Assert.Equal(firstCandidate.Id, stored.Id);
                Assert.True(stored.MatchesFingerprint(duplicateCandidate.RequestFingerprint));
                await secondTransaction.RollbackAsync();
            }
        }
    }

    [Fact]
    public async Task RefreshRejectsTokenThatExpiresWhileWaitingForFamilyLock()
    {
        var beforeLockWait = new DateTime(2026, 8, 15, 4, 30, 0, DateTimeKind.Utc);
        var expiresAtUtc = beforeLockWait.AddSeconds(1);
        var administratorId = Guid.NewGuid();
        var familyId = Guid.NewGuid();
        var tokenHash = CreateHash(7);
        var administrator = new Administrator(
            administratorId,
            AdministratorUserName.Create("refresh-lock-wait"),
            "Refresh lock wait administrator",
            "test-password-hash",
            beforeLockWait.AddDays(-1));
        var session = new RefreshTokenSession(
            Guid.NewGuid(),
            administratorId,
            familyId,
            tokenHash,
            beforeLockWait.AddDays(-1),
            expiresAtUtc,
            "203.0.113.30");
        var clock = new AdvancingRefreshClock(beforeLockWait);
        var sessionRepository = new LockWaitRefreshTokenSessionRepository(
            session,
            clock,
            expiresAtUtc);
        var auditRepository = new RecordingRefreshAuditRepository();
        var service = new AdminAccessAppService(
            new TestUnitOfWorkManager(),
            new LockWaitAdministratorRepository(administrator),
            sessionRepository,
            auditRepository,
            new RecordingPasswordHashingService(),
            new ThrowingAccessTokenIssuer(),
            new FixedRefreshTokenService("refresh-token", tokenHash),
            new RefreshTokenFamilyPolicy(),
            new TestRequestAuditContextAccessor(
                new RequestAuditContext("203.0.113.31", "refresh-lock-wait", "m35-04-t01")),
            new TestCurrentUser(false, Guid.Empty, string.Empty),
            new SequentialGuidGenerator(),
            clock);

        var exception = await Assert.ThrowsAsync<AdminAccessException>(
            () => service.RefreshAsync("refresh-token"));

        Assert.Equal(AdminAccessErrorCodes.RefreshTokenExpired, exception.Code);
        Assert.True(session.IsRevoked);
        Assert.Equal(RefreshTokenRevocationReason.Expired, session.RevocationReason);
        Assert.Equal(expiresAtUtc, session.RevokedAtUtc);
        Assert.Empty(sessionRepository.AddedSessions);
        Assert.Single(auditRepository.Records);
        Assert.Equal(expiresAtUtc, auditRepository.Records[0].OccurredAtUtc);
    }

    [Fact]
    public async Task SessionFamilyPagesProjectActiveExpiredAndRevokedTerminalStates()
    {
        var now = new DateTime(2026, 8, 15, 8, 0, 0, DateTimeKind.Utc);
        var administratorId = Guid.NewGuid();
        var activeFamilyId = Guid.NewGuid();
        var expiredFamilyId = Guid.NewGuid();
        var revokedFamilyId = Guid.NewGuid();
        var activeRootId = Guid.NewGuid();
        var activeTerminalId = Guid.NewGuid();
        var activeRoot = new RefreshTokenSession(
            activeRootId,
            administratorId,
            activeFamilyId,
            CreateHash(21),
            now.AddHours(-2),
            now.AddDays(1),
            "203.0.113.40");
        activeRoot.Rotate(activeTerminalId, now.AddHours(-1), "203.0.113.41");
        var activeTerminal = new RefreshTokenSession(
            activeTerminalId,
            administratorId,
            activeFamilyId,
            CreateHash(53),
            now.AddHours(-1),
            now.AddDays(1),
            "203.0.113.42");
        var expiredTerminal = new RefreshTokenSession(
            Guid.NewGuid(),
            administratorId,
            expiredFamilyId,
            CreateHash(85),
            now.AddDays(-2),
            now.AddMinutes(-1),
            "203.0.113.43");
        var revokedTerminal = new RefreshTokenSession(
            Guid.NewGuid(),
            administratorId,
            revokedFamilyId,
            CreateHash(117),
            now.AddHours(-3),
            now.AddDays(1),
            "203.0.113.44");
        var revokedAtUtc = now.AddMinutes(-30);
        revokedTerminal.Revoke(
            RefreshTokenRevocationReason.Logout,
            revokedAtUtc,
            "203.0.113.45");

        await using (var seedContext = _fixture.CreateDbContext())
        {
            await seedContext.Administrators.AddAsync(new Administrator(
                administratorId,
                AdministratorUserName.Create($"session-states-{Guid.NewGuid():N}"),
                "Session-state projection administrator",
                "test-password-hash",
                now.AddDays(-3)));
            await seedContext.RefreshTokenSessions.AddRangeAsync(
                activeRoot,
                activeTerminal,
                expiredTerminal,
                revokedTerminal);
            await seedContext.SaveChangesAsync();
        }

        await using var dbContext = _fixture.CreateDbContext();
        var repository = new AdminAccessManagementRepository(
            dbContext,
            new RefreshTokenFamilyPolicy());

        var active = Assert.Single((await repository.GetSessionFamilyPageAsync(
            administratorId,
            AdminSessionFamilyStateSnapshot.Active,
            now,
            skipCount: 0,
            maxResultCount: 10)).Items);
        var expired = Assert.Single((await repository.GetSessionFamilyPageAsync(
            administratorId,
            AdminSessionFamilyStateSnapshot.Expired,
            now,
            skipCount: 0,
            maxResultCount: 10)).Items);
        var revoked = Assert.Single((await repository.GetSessionFamilyPageAsync(
            administratorId,
            AdminSessionFamilyStateSnapshot.Revoked,
            now,
            skipCount: 0,
            maxResultCount: 10)).Items);

        Assert.Equal(activeFamilyId, active.FamilyId);
        Assert.Equal(administratorId, active.AdministratorId);
        Assert.Equal(now.AddHours(-2), active.FirstCreatedAtUtc);
        Assert.Equal(now.AddHours(-1), active.LastActivityAtUtc);
        Assert.Equal("203.0.113.41", active.LastIpAddress);
        Assert.Equal(now.AddDays(1), active.ExpiresAtUtc);
        Assert.Equal(AdminSessionFamilyStateSnapshot.Active, active.State);
        Assert.Null(active.RevokedAtUtc);
        Assert.Null(active.RevocationReason);

        Assert.Equal(expiredFamilyId, expired.FamilyId);
        Assert.Equal(now.AddDays(-2), expired.FirstCreatedAtUtc);
        Assert.Equal(now.AddDays(-2), expired.LastActivityAtUtc);
        Assert.Equal(now.AddMinutes(-1), expired.ExpiresAtUtc);
        Assert.Equal("203.0.113.43", expired.LastIpAddress);
        Assert.Equal(AdminSessionFamilyStateSnapshot.Expired, expired.State);
        Assert.Null(expired.RevokedAtUtc);
        Assert.Null(expired.RevocationReason);

        Assert.Equal(revokedFamilyId, revoked.FamilyId);
        Assert.Equal(now.AddHours(-3), revoked.FirstCreatedAtUtc);
        Assert.Equal(revokedAtUtc, revoked.LastActivityAtUtc);
        Assert.Equal("203.0.113.45", revoked.LastIpAddress);
        Assert.Equal(AdminSessionFamilyStateSnapshot.Revoked, revoked.State);
        Assert.Equal(revokedAtUtc, revoked.RevokedAtUtc);
        Assert.Equal(RefreshTokenRevocationReason.Logout, revoked.RevocationReason);
    }

    [Fact]
    public async Task ExactFamilyRevocationPersistsOnceAndLeavesOtherFamiliesUntouched()
    {
        var now = new DateTime(2026, 8, 15, 8, 30, 0, DateTimeKind.Utc);
        var administratorId = Guid.NewGuid();
        var targetFamilyId = Guid.NewGuid();
        var unrelatedExpiredFamilyId = Guid.NewGuid();
        var targetRootId = Guid.NewGuid();
        var targetTerminalId = Guid.NewGuid();
        var targetRoot = new RefreshTokenSession(
            targetRootId,
            administratorId,
            targetFamilyId,
            CreateHash(149),
            now.AddHours(-2),
            now.AddDays(1),
            "203.0.113.50");
        targetRoot.Rotate(targetTerminalId, now.AddHours(-1), "203.0.113.51");
        var targetTerminal = new RefreshTokenSession(
            targetTerminalId,
            administratorId,
            targetFamilyId,
            CreateHash(181),
            now.AddHours(-1),
            now.AddDays(1),
            "203.0.113.52");
        var unrelatedExpiredTerminal = new RefreshTokenSession(
            Guid.NewGuid(),
            administratorId,
            unrelatedExpiredFamilyId,
            CreateHash(213),
            now.AddDays(-2),
            now.AddMinutes(-1),
            "203.0.113.53");

        await using (var seedContext = _fixture.CreateDbContext())
        {
            await seedContext.Administrators.AddAsync(new Administrator(
                administratorId,
                AdministratorUserName.Create($"session-revoke-{Guid.NewGuid():N}"),
                "Exact-revocation administrator",
                "test-password-hash",
                now.AddDays(-3)));
            await seedContext.RefreshTokenSessions.AddRangeAsync(
                targetRoot,
                targetTerminal,
                unrelatedExpiredTerminal);
            await seedContext.SaveChangesAsync();
        }

        var firstRevokedAtUtc = now;
        await using (var firstContext = _fixture.CreateDbContext())
        {
            await using var transaction = await firstContext.Database.BeginTransactionAsync();
            var repository = new AdminAccessManagementRepository(
                firstContext,
                new RefreshTokenFamilyPolicy());
            var lockedRows = await repository.GetSessionFamilyForUpdateAsync(targetFamilyId);
            var transition = new RefreshTokenFamilyPolicy().Revoke(
                lockedRows,
                RefreshTokenRevocationReason.ForcedByAdministrator,
                firstRevokedAtUtc,
                "203.0.113.54");

            Assert.True(transition.Changed);
            Assert.Equal(targetFamilyId, transition.FamilyId);
            Assert.Equal(administratorId, transition.AdministratorId);
            await repository.AddAuditAsync(new AdminAccessAuditRecord(
                Guid.NewGuid(),
                AdminAccessAuditAction.SessionFamilyRevoked,
                firstRevokedAtUtc,
                succeeded: true,
                administratorId: administratorId,
                actorAdministratorId: administratorId,
                sessionFamilyId: targetFamilyId,
                correlationId: $"revoke-{Guid.NewGuid():N}"));
            await firstContext.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using (var repeatContext = _fixture.CreateDbContext())
        {
            await using var transaction = await repeatContext.Database.BeginTransactionAsync();
            var repository = new AdminAccessManagementRepository(
                repeatContext,
                new RefreshTokenFamilyPolicy());
            var lockedRows = await repository.GetSessionFamilyForUpdateAsync(targetFamilyId);
            var repeatedTransition = new RefreshTokenFamilyPolicy().Revoke(
                lockedRows,
                RefreshTokenRevocationReason.ForcedByAdministrator,
                now.AddMinutes(1),
                "203.0.113.55");

            Assert.False(repeatedTransition.Changed);
            await repeatContext.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using var verificationContext = _fixture.CreateDbContext();
        var persistedTargetRows = await verificationContext.RefreshTokenSessions
            .AsNoTracking()
            .Where(session => session.FamilyId == targetFamilyId)
            .OrderBy(session => session.CreatedAtUtc)
            .ThenBy(session => session.Id)
            .ToArrayAsync();
        var persistedTargetTerminal = Assert.Single(
            persistedTargetRows,
            session => session.Id == targetTerminalId);
        var persistedUnrelatedExpiredTerminal = await verificationContext.RefreshTokenSessions
            .AsNoTracking()
            .SingleAsync(session => session.FamilyId == unrelatedExpiredFamilyId);
        var audits = await verificationContext.AdminAccessAuditRecords
            .AsNoTracking()
            .Where(record => record.Action == AdminAccessAuditAction.SessionFamilyRevoked &&
                record.SessionFamilyId == targetFamilyId)
            .ToArrayAsync();

        Assert.Equal(new[] { targetRootId, targetTerminalId }, persistedTargetRows.Select(session => session.Id));
        Assert.All(persistedTargetRows, session => Assert.True(session.IsRevoked));
        Assert.Equal(RefreshTokenRevocationReason.Rotated, persistedTargetRows[0].RevocationReason);
        Assert.Equal(firstRevokedAtUtc, persistedTargetTerminal.RevokedAtUtc);
        Assert.Equal(RefreshTokenRevocationReason.ForcedByAdministrator, persistedTargetTerminal.RevocationReason);
        Assert.Equal("203.0.113.54", persistedTargetTerminal.RevokedFromIpAddress);
        Assert.False(persistedUnrelatedExpiredTerminal.IsRevoked);
        Assert.Null(persistedUnrelatedExpiredTerminal.RevokedAtUtc);
        Assert.Null(persistedUnrelatedExpiredTerminal.RevocationReason);
        Assert.Single(audits);
    }

    private static byte[] CreateHash(byte seed)
    {
        return Enumerable.Range(0, 32).Select(index => unchecked((byte)(seed + index))).ToArray();
    }

    private sealed class LockWaitAdministratorRepository : IAdministratorRepository
    {
        private readonly Administrator _administrator;

        public LockWaitAdministratorRepository(Administrator administrator)
        {
            _administrator = administrator;
        }

        public Task<bool> AnyAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<Administrator?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Administrator?>(id == _administrator.Id ? _administrator : null);

        public Task<Administrator?> FindByIdForUpdateAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Administrator?>(id == _administrator.Id ? _administrator : null);

        public Task<Administrator?> FindByNormalizedUserNameAsync(
            string normalizedUserName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Administrator?>(null);

        public Task<Administrator?> FindByNormalizedUserNameForUpdateAsync(
            string normalizedUserName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Administrator?>(null);

        public Task<AdministratorAuthorizationSnapshot?> GetAuthorizationSnapshotAsync(
            Guid administratorId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<AdministratorAuthorizationSnapshot?>(null);

        public Task AddAsync(Administrator administrator, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class LockWaitRefreshTokenSessionRepository : IRefreshTokenSessionRepository
    {
        private readonly RefreshTokenSession _session;
        private readonly AdvancingRefreshClock _clock;
        private readonly DateTime _timeAfterLockWait;

        public LockWaitRefreshTokenSessionRepository(
            RefreshTokenSession session,
            AdvancingRefreshClock clock,
            DateTime timeAfterLockWait)
        {
            _session = session;
            _clock = clock;
            _timeAfterLockWait = timeAfterLockWait;
        }

        public List<RefreshTokenSession> AddedSessions { get; } = [];

        public Task<RefreshTokenSession?> FindByTokenHashAsync(
            byte[] tokenHash,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<RefreshTokenSession?>(
                tokenHash.SequenceEqual(_session.TokenHash) ? _session : null);

        public Task<IReadOnlyList<RefreshTokenSession>> GetFamilyForUpdateAsync(
            Guid familyId,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(_session.FamilyId, familyId);
            _clock.AdvanceTo(_timeAfterLockWait);
            return Task.FromResult<IReadOnlyList<RefreshTokenSession>>([_session]);
        }

        public Task AddAsync(RefreshTokenSession session, CancellationToken cancellationToken = default)
        {
            AddedSessions.Add(session);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingRefreshAuditRepository : IAdminAccessAuditRepository
    {
        public List<AdminAccessAuditRecord> Records { get; } = [];

        public Task AddAsync(
            AdminAccessAuditRecord auditRecord,
            CancellationToken cancellationToken = default)
        {
            Records.Add(auditRecord);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedRefreshTokenService : IRefreshTokenService
    {
        private readonly string _token;
        private readonly byte[] _tokenHash;

        public FixedRefreshTokenService(string token, byte[] tokenHash)
        {
            _token = token;
            _tokenHash = tokenHash;
        }

        public IssuedRefreshToken Create() =>
            throw new InvalidOperationException("An expired refresh token must not be rotated.");

        public bool TryHash(string? token, out byte[] tokenHash)
        {
            tokenHash = string.Equals(token, _token, StringComparison.Ordinal)
                ? _tokenHash.ToArray()
                : [];
            return tokenHash.Length > 0;
        }
    }

    private sealed class ThrowingAccessTokenIssuer : IAccessTokenIssuer
    {
        public string Issue(
            Guid administratorId,
            string userName,
            DateTime issuedAtUtc,
            DateTime expiresAtUtc) =>
            throw new InvalidOperationException("An expired refresh token must not issue an access token.");
    }

    private sealed class AdvancingRefreshClock : IClock
    {
        private DateTime _utcNow;

        public AdvancingRefreshClock(DateTime utcNow)
        {
            _utcNow = utcNow;
        }

        public DateTime Now => UtcNow.ToLocalTime();

        public DateTime UtcNow => _utcNow;

        public void AdvanceTo(DateTime utcNow)
        {
            _utcNow = utcNow;
        }
    }
}
