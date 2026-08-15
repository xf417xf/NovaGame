using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NovaGame.Application.Operations.AdminAccess;
using NovaGame.Domain.Operations.AdminAccess;
using NovaGame.Domain.Shared.Operations.AdminAccess;
using NovaGame.Host.EntityFrameworkCore.Operations.AdminAccess;
using Xunit;

namespace NovaGame.EntityFrameworkCore.Tests;

[Collection(MySqlDatabaseCollection.Name)]
public sealed class AdminAccessManagementQueryTests
{
    private readonly MySqlDatabaseFixture _fixture;

    public AdminAccessManagementQueryTests(MySqlDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AdministratorRoleAndAuditQueriesUseExactFiltersAndDatabasePaging()
    {
        var now = new DateTime(2026, 8, 15, 5, 0, 0, DateTimeKind.Utc);
        var suffix = Guid.NewGuid().ToString("N");
        var prefix = $"query-{suffix}";
        var roleId = Guid.NewGuid();
        var inactiveRoleId = Guid.NewGuid();
        var firstAdministratorId = Guid.NewGuid();
        var secondAdministratorId = Guid.NewGuid();
        var literalPercentAdministratorId = Guid.NewGuid();
        var activeRole = new AdminRole(roleId, $"role-{suffix}", now);
        var inactiveRole = new AdminRole(inactiveRoleId, $"inactive-{suffix}", now);
        _ = inactiveRole.SetActiveState(1, false, now.AddMinutes(1));
        var firstAdministrator = new Administrator(
            firstAdministratorId,
            AdministratorUserName.Create($"{prefix}-a"),
            "First query administrator",
            "test-password-hash",
            now,
            [new AdministratorRoleTarget(Guid.NewGuid(), roleId)]);
        var secondAdministrator = new Administrator(
            secondAdministratorId,
            AdministratorUserName.Create($"{prefix}-b"),
            "Second query administrator",
            "test-password-hash",
            now,
            [new AdministratorRoleTarget(Guid.NewGuid(), roleId)]);
        var literalPercentAdministrator = new Administrator(
            literalPercentAdministratorId,
            AdministratorUserName.Create($"{prefix}%literal"),
            "Literal percent query administrator",
            "test-password-hash",
            now,
            [new AdministratorRoleTarget(Guid.NewGuid(), inactiveRoleId)]);
        var matchingCorrelation = $"Correlation-{suffix}";

        await using (var seedContext = _fixture.CreateDbContext())
        {
            await seedContext.AdminRoles.AddRangeAsync(activeRole, inactiveRole);
            await seedContext.Administrators.AddRangeAsync(
                firstAdministrator,
                secondAdministrator,
                literalPercentAdministrator);
            await seedContext.AdminAccessAuditRecords.AddRangeAsync(
                new AdminAccessAuditRecord(
                    Guid.NewGuid(),
                    AdminAccessAuditAction.AdministratorCreated,
                    now.AddMinutes(2),
                    succeeded: true,
                    administratorId: firstAdministratorId,
                    actorAdministratorId: secondAdministratorId,
                    roleId: roleId,
                    correlationId: matchingCorrelation),
                new AdminAccessAuditRecord(
                    Guid.NewGuid(),
                    AdminAccessAuditAction.AdministratorCreated,
                    now.AddMinutes(3),
                    succeeded: true,
                    administratorId: secondAdministratorId,
                    actorAdministratorId: firstAdministratorId,
                    roleId: roleId,
                    correlationId: matchingCorrelation.ToLowerInvariant()),
                new AdminAccessAuditRecord(
                    Guid.NewGuid(),
                    AdminAccessAuditAction.AdministratorCreated,
                    now.AddDays(1),
                    succeeded: true,
                    administratorId: firstAdministratorId,
                    actorAdministratorId: secondAdministratorId,
                    roleId: roleId,
                    correlationId: matchingCorrelation));
            await seedContext.SaveChangesAsync();
        }

        await using var dbContext = _fixture.CreateDbContext();
        var repository = new AdminAccessManagementRepository(
            dbContext,
            new RefreshTokenFamilyPolicy());

        var administratorPage = await repository.GetAdministratorPageAsync(
            prefix.ToUpperInvariant(),
            isActive: true,
            roleId,
            skipCount: 1,
            maxResultCount: 1);
        var literalPercentPage = await repository.GetAdministratorPageAsync(
            (prefix + "%").ToUpperInvariant(),
            isActive: null,
            roleId: null,
            skipCount: 0,
            maxResultCount: 10);
        var rolePage = await repository.GetRolePageAsync(
            $"ROLE-{suffix}".ToUpperInvariant(),
            isActive: true,
            isBuiltIn: false,
            registeredPermissionNames: [],
            skipCount: 0,
            maxResultCount: 10);
        var auditPage = await repository.GetAuditPageAsync(
            new AdminAccessAuditQuery(
                now,
                now.AddDays(1),
                AdminAccessAuditAction.AdministratorCreated,
                Succeeded: true,
                ActorAdministratorId: secondAdministratorId,
                SubjectAdministratorId: firstAdministratorId,
                RoleId: roleId,
                CorrelationId: matchingCorrelation),
            skipCount: 0,
            maxResultCount: 10);

        Assert.Equal(2, administratorPage.TotalCount);
        var administrator = Assert.Single(administratorPage.Items);
        Assert.Equal(secondAdministratorId, administrator.Id);
        Assert.Equal(roleId, Assert.Single(administrator.Roles).Id);
        Assert.Equal(literalPercentAdministratorId, Assert.Single(literalPercentPage.Items).Id);
        Assert.Equal(1, literalPercentPage.TotalCount);
        Assert.Equal(roleId, Assert.Single(rolePage.Items).Id);
        Assert.Equal(1, rolePage.TotalCount);
        var audit = Assert.Single(auditPage.Items);
        Assert.Equal(matchingCorrelation, audit.CorrelationId);
        Assert.Equal(firstAdministratorId, audit.SubjectAdministratorId);
    }

    [Fact]
    public async Task PermissionChecksAndRoleSnapshotsUseOnlyActiveRegisteredPersistedGrants()
    {
        var now = new DateTime(2026, 8, 15, 5, 30, 0, DateTimeKind.Utc);
        var suffix = Guid.NewGuid().ToString("N");
        var administratorId = Guid.NewGuid();
        var activeRoleId = Guid.NewGuid();
        var inactiveRoleId = Guid.NewGuid();
        var unregisteredPermission = "Operations.AdminAccess.LegacyStored";
        var activeRole = new AdminRole(activeRoleId, $"permission-active-{suffix}", now);
        var inactiveRole = new AdminRole(inactiveRoleId, $"permission-inactive-{suffix}", now);
        _ = inactiveRole.SetActiveState(1, false, now.AddMinutes(1));
        var administrator = new Administrator(
            administratorId,
            AdministratorUserName.Create($"permission-{suffix}"),
            "Permission projection administrator",
            "test-password-hash",
            now,
            [
                new AdministratorRoleTarget(Guid.NewGuid(), activeRoleId),
                new AdministratorRoleTarget(Guid.NewGuid(), inactiveRoleId)
            ]);

        await using (var seedContext = _fixture.CreateDbContext())
        {
            await seedContext.AdminRoles.AddRangeAsync(activeRole, inactiveRole);
            await seedContext.Administrators.AddAsync(administrator);
            await seedContext.SaveChangesAsync();
            await seedContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO `admin_role_permissions` (
                    `Id`,
                    `RoleId`,
                    `Name`,
                    `GrantedAtUtc`,
                    `GrantedByAdministratorId`)
                VALUES (
                    {Guid.NewGuid().ToString("D")},
                    {activeRoleId.ToString("D")},
                    {AdminAccessPermissions.Access},
                    {now},
                    NULL)
                """);
            await seedContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO `admin_role_permissions` (
                    `Id`,
                    `RoleId`,
                    `Name`,
                    `GrantedAtUtc`,
                    `GrantedByAdministratorId`)
                VALUES (
                    {Guid.NewGuid().ToString("D")},
                    {activeRoleId.ToString("D")},
                    {unregisteredPermission},
                    {now},
                    NULL)
                """);
            await seedContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO `admin_role_permissions` (
                    `Id`,
                    `RoleId`,
                    `Name`,
                    `GrantedAtUtc`,
                    `GrantedByAdministratorId`)
                VALUES (
                    {Guid.NewGuid().ToString("D")},
                    {inactiveRoleId.ToString("D")},
                    {AdminAccessPermissions.AdministratorsManage},
                    {now},
                    NULL)
                """);
        }

        await using var dbContext = _fixture.CreateDbContext();
        var permissionChecker = new DatabasePermissionChecker(
            new TestCurrentUser(true, administratorId, "permission-projection"),
            new CountingPermissionDefinitionManager(
            [
                AdminAccessPermissions.Access,
                AdminAccessPermissions.AdministratorsManage
            ]),
            new AdministratorRepository(dbContext));
        var repository = new AdminAccessManagementRepository(
            dbContext,
            new RefreshTokenFamilyPolicy());

        Assert.True(await permissionChecker.IsGrantedAsync(AdminAccessPermissions.Access));
        Assert.False(await permissionChecker.IsGrantedAsync(AdminAccessPermissions.AdministratorsManage));
        Assert.False(await permissionChecker.IsGrantedAsync(unregisteredPermission));

        var rolePage = await repository.GetRolePageAsync(
            activeRole.NormalizedName,
            isActive: true,
            isBuiltIn: false,
            registeredPermissionNames: [AdminAccessPermissions.Access],
            skipCount: 0,
            maxResultCount: 10);

        var role = Assert.Single(rolePage.Items);
        Assert.Equal(activeRoleId, role.Id);
        Assert.Equal([AdminAccessPermissions.Access], role.PermissionNames);
        Assert.Equal(1, rolePage.TotalCount);
    }

    [Fact]
    public async Task ManagementListReadSnapshotUsesExplicitMySqlRepeatableRead()
    {
        await using var dbContext = _fixture.CreateDbContext();
        var repository = new AdminAccessManagementRepository(
            dbContext,
            new RefreshTokenFamilyPolicy());

        var isolationLevel = await repository.ExecuteAsync(async cancellationToken =>
        {
            _ = await repository.GetAdministratorPageAsync(
                normalizedUserNamePrefix: null,
                isActive: null,
                roleId: null,
                skipCount: 0,
                maxResultCount: 1,
                cancellationToken: cancellationToken);
            var transaction = Assert.IsAssignableFrom<IDbContextTransaction>(
                dbContext.Database.CurrentTransaction);
            await using var command = dbContext.Database.GetDbConnection().CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = "SELECT @@transaction_isolation";
            return Convert.ToString(await command.ExecuteScalarAsync(cancellationToken));
        });

        Assert.Equal("REPEATABLE-READ", isolationLevel);
    }

    [Fact]
    public async Task ManagementListReadSnapshotKeepsCountAndPageConsistentAcrossAConcurrentCommit()
    {
        var now = new DateTime(2026, 8, 15, 7, 0, 0, DateTimeKind.Utc);
        var suffix = Guid.NewGuid().ToString("N");
        var prefix = $"snapshot-{suffix}";
        var normalizedPrefix = prefix.ToUpperInvariant();
        var firstAdministratorId = Guid.NewGuid();
        var secondAdministratorId = Guid.NewGuid();

        await using (var seedContext = _fixture.CreateDbContext())
        {
            await seedContext.Administrators.AddAsync(new Administrator(
                firstAdministratorId,
                AdministratorUserName.Create($"{prefix}-first"),
                "First snapshot administrator",
                "test-password-hash",
                now));
            await seedContext.SaveChangesAsync();
        }

        await using var readerContext = _fixture.CreateDbContext();
        var repository = new AdminAccessManagementRepository(
            readerContext,
            new RefreshTokenFamilyPolicy());
        var snapshotResult = await repository.ExecuteAsync(async cancellationToken =>
        {
            var totalBeforeCommit = await readerContext.Administrators
                .AsNoTracking()
                .LongCountAsync(
                    administrator => administrator.NormalizedUserName.StartsWith(normalizedPrefix),
                    cancellationToken);

            await using (var writerContext = _fixture.CreateDbContext())
            {
                await writerContext.Administrators.AddAsync(new Administrator(
                    secondAdministratorId,
                    AdministratorUserName.Create($"{prefix}-second"),
                    "Second snapshot administrator",
                    "test-password-hash",
                    now.AddMinutes(1)), cancellationToken);
                await writerContext.SaveChangesAsync(cancellationToken);
            }

            var page = await repository.GetAdministratorPageAsync(
                normalizedPrefix,
                isActive: null,
                roleId: null,
                skipCount: 0,
                maxResultCount: 10,
                cancellationToken: cancellationToken);
            return (TotalBeforeCommit: totalBeforeCommit, Page: page);
        });

        Assert.Equal(1, snapshotResult.TotalBeforeCommit);
        Assert.Equal(1, snapshotResult.Page.TotalCount);
        Assert.Equal(firstAdministratorId, Assert.Single(snapshotResult.Page.Items).Id);

        await using var verificationContext = _fixture.CreateDbContext();
        var committedCount = await verificationContext.Administrators.LongCountAsync(
            administrator => administrator.NormalizedUserName.StartsWith(normalizedPrefix));
        Assert.Equal(2, committedCount);
    }

    [Fact]
    public async Task RoleSessionAndAuditPagesKeepOneSnapshotAcrossAConcurrentCommit()
    {
        var now = new DateTime(2026, 8, 15, 7, 30, 0, DateTimeKind.Utc);
        var suffix = Guid.NewGuid().ToString("N");
        var rolePrefix = $"snapshot-role-{suffix}";
        var normalizedRolePrefix = rolePrefix.ToUpperInvariant();
        var administratorId = Guid.NewGuid();
        var firstRoleId = Guid.NewGuid();
        var secondRoleId = Guid.NewGuid();
        var firstFamilyId = Guid.NewGuid();
        var secondFamilyId = Guid.NewGuid();
        var correlationId = $"snapshot-correlation-{suffix}";
        var firstRole = new AdminRole(firstRoleId, $"{rolePrefix}-first", now);
        var administrator = new Administrator(
            administratorId,
            AdministratorUserName.Create($"snapshot-query-{suffix}"),
            "Snapshot query administrator",
            "test-password-hash",
            now,
            [new AdministratorRoleTarget(Guid.NewGuid(), firstRoleId)]);
        var firstSession = new RefreshTokenSession(
            Guid.NewGuid(),
            administratorId,
            firstFamilyId,
            CreateHash(193),
            now.AddMinutes(-2),
            now.AddDays(1),
            "203.0.113.60");

        await using (var seedContext = _fixture.CreateDbContext())
        {
            await seedContext.AdminRoles.AddAsync(firstRole);
            await seedContext.Administrators.AddAsync(administrator);
            await seedContext.RefreshTokenSessions.AddAsync(firstSession);
            await seedContext.AdminAccessAuditRecords.AddAsync(new AdminAccessAuditRecord(
                Guid.NewGuid(),
                AdminAccessAuditAction.AdministratorCreated,
                now,
                succeeded: true,
                administratorId: administratorId,
                actorAdministratorId: administratorId,
                roleId: firstRoleId,
                correlationId: correlationId));
            await seedContext.SaveChangesAsync();
        }

        await using var readerContext = _fixture.CreateDbContext();
        var repository = new AdminAccessManagementRepository(
            readerContext,
            new RefreshTokenFamilyPolicy());
        var auditQuery = new AdminAccessAuditQuery(
            now.AddHours(-1),
            now.AddHours(1),
            AdminAccessAuditAction.AdministratorCreated,
            Succeeded: true,
            ActorAdministratorId: administratorId,
            SubjectAdministratorId: administratorId,
            RoleId: firstRoleId,
            CorrelationId: correlationId);
        var snapshotResult = await repository.ExecuteAsync(async cancellationToken =>
        {
            var initialRolePage = await repository.GetRolePageAsync(
                normalizedRolePrefix,
                isActive: true,
                isBuiltIn: false,
                registeredPermissionNames: [],
                skipCount: 0,
                maxResultCount: 10,
                cancellationToken: cancellationToken);
            var initialSessionPage = await repository.GetSessionFamilyPageAsync(
                administratorId,
                AdminSessionFamilyStateSnapshot.Active,
                now,
                skipCount: 0,
                maxResultCount: 10,
                cancellationToken: cancellationToken);
            var initialAuditPage = await repository.GetAuditPageAsync(
                auditQuery,
                skipCount: 0,
                maxResultCount: 10,
                cancellationToken: cancellationToken);

            await using (var writerContext = _fixture.CreateDbContext())
            {
                await writerContext.AdminRoles.AddAsync(new AdminRole(
                    secondRoleId,
                    $"{rolePrefix}-second",
                    now.AddMinutes(1)), cancellationToken);
                await writerContext.RefreshTokenSessions.AddAsync(new RefreshTokenSession(
                    Guid.NewGuid(),
                    administratorId,
                    secondFamilyId,
                    CreateHash(225),
                    now.AddMinutes(1),
                    now.AddDays(1),
                    "203.0.113.61"), cancellationToken);
                await writerContext.AdminAccessAuditRecords.AddAsync(new AdminAccessAuditRecord(
                    Guid.NewGuid(),
                    AdminAccessAuditAction.AdministratorCreated,
                    now.AddMinutes(1),
                    succeeded: true,
                    administratorId: administratorId,
                    actorAdministratorId: administratorId,
                    roleId: firstRoleId,
                    correlationId: correlationId), cancellationToken);
                await writerContext.SaveChangesAsync(cancellationToken);
            }

            var rolePage = await repository.GetRolePageAsync(
                normalizedRolePrefix,
                isActive: true,
                isBuiltIn: false,
                registeredPermissionNames: [],
                skipCount: 0,
                maxResultCount: 10,
                cancellationToken: cancellationToken);
            var sessionPage = await repository.GetSessionFamilyPageAsync(
                administratorId,
                AdminSessionFamilyStateSnapshot.Active,
                now,
                skipCount: 0,
                maxResultCount: 10,
                cancellationToken: cancellationToken);
            var auditPage = await repository.GetAuditPageAsync(
                auditQuery,
                skipCount: 0,
                maxResultCount: 10,
                cancellationToken: cancellationToken);
            return (initialRolePage, initialSessionPage, initialAuditPage, rolePage, sessionPage, auditPage);
        });

        Assert.Equal(1, snapshotResult.initialRolePage.TotalCount);
        Assert.Equal(firstRoleId, Assert.Single(snapshotResult.initialRolePage.Items).Id);
        Assert.Equal(1, snapshotResult.initialSessionPage.TotalCount);
        Assert.Equal(firstFamilyId, Assert.Single(snapshotResult.initialSessionPage.Items).FamilyId);
        Assert.Equal(1, snapshotResult.initialAuditPage.TotalCount);
        Assert.Single(snapshotResult.initialAuditPage.Items);
        Assert.Equal(1, snapshotResult.rolePage.TotalCount);
        Assert.Equal(firstRoleId, Assert.Single(snapshotResult.rolePage.Items).Id);
        Assert.Equal(1, snapshotResult.sessionPage.TotalCount);
        Assert.Equal(firstFamilyId, Assert.Single(snapshotResult.sessionPage.Items).FamilyId);
        Assert.Equal(1, snapshotResult.auditPage.TotalCount);
        Assert.Single(snapshotResult.auditPage.Items);

        await using var verificationContext = _fixture.CreateDbContext();
        Assert.Equal(
            2,
            await verificationContext.AdminRoles.LongCountAsync(role =>
                role.NormalizedName.StartsWith(normalizedRolePrefix)));
        Assert.Equal(
            2,
            await verificationContext.RefreshTokenSessions
                .Select(session => session.FamilyId)
                .Distinct()
                .LongCountAsync(familyId => familyId == firstFamilyId || familyId == secondFamilyId));
        Assert.Equal(
            2,
            await verificationContext.AdminAccessAuditRecords.LongCountAsync(record =>
                record.CorrelationId == correlationId));
    }

    [Fact]
    public async Task SessionFamilyPageFailsClosedWhenCorruptionFallsOutsideTheRequestedPage()
    {
        var now = new DateTime(2026, 8, 15, 6, 0, 0, DateTimeKind.Utc);
        var suffix = Guid.NewGuid().ToString("N");
        var administratorId = Guid.NewGuid();
        var otherAdministratorId = Guid.NewGuid();
        var visibleFamilyId = Guid.NewGuid();
        var malformedFamilyId = Guid.NewGuid();
        var malformedReplacementId = Guid.NewGuid();
        var visibleSession = new RefreshTokenSession(
            Guid.NewGuid(),
            administratorId,
            visibleFamilyId,
            CreateHash(1),
            now.AddMinutes(-1),
            now.AddDays(7),
            "203.0.113.20");
        var malformedRoot = new RefreshTokenSession(
            Guid.NewGuid(),
            administratorId,
            malformedFamilyId,
            CreateHash(33),
            now.AddMinutes(-4),
            now.AddDays(7),
            "203.0.113.21");
        malformedRoot.Rotate(malformedReplacementId, now.AddMinutes(-2), "203.0.113.22");
        var malformedReplacement = new RefreshTokenSession(
            malformedReplacementId,
            otherAdministratorId,
            malformedFamilyId,
            CreateHash(65),
            now.AddMinutes(-2),
            now.AddDays(7),
            "203.0.113.23");

        await using (var seedContext = _fixture.CreateDbContext())
        {
            await seedContext.Administrators.AddRangeAsync(
                new Administrator(
                    administratorId,
                    AdministratorUserName.Create($"query-owner-{suffix}"),
                    "Query owner",
                    "test-password-hash",
                    now.AddHours(-1)),
                new Administrator(
                    otherAdministratorId,
                    AdministratorUserName.Create($"query-other-{suffix}"),
                    "Query other owner",
                    "test-password-hash",
                    now.AddHours(-1)));
            await seedContext.RefreshTokenSessions.AddRangeAsync(
                visibleSession,
                malformedRoot,
                malformedReplacement);
            await seedContext.SaveChangesAsync();
        }

        await using var dbContext = _fixture.CreateDbContext();
        var repository = new AdminAccessManagementRepository(
            dbContext,
            new RefreshTokenFamilyPolicy());

        var exception = await Assert.ThrowsAsync<AdminAccessManagementDomainException>(() =>
            repository.GetSessionFamilyPageAsync(
                administratorId,
                AdminSessionFamilyStateSnapshot.Active,
                now,
                skipCount: 0,
                maxResultCount: 1));

        Assert.Equal(AdminAccessManagementErrorCodes.InvariantViolation, exception.Code);
    }

    [Fact]
    public async Task SessionFamilyPageFailsClosedWhenCorruptionIsExcludedByTheRequestedState()
    {
        var now = new DateTime(2026, 8, 15, 6, 30, 0, DateTimeKind.Utc);
        var suffix = Guid.NewGuid().ToString("N");
        var administratorId = Guid.NewGuid();
        var otherAdministratorId = Guid.NewGuid();
        var activeFamilyId = Guid.NewGuid();
        var malformedFamilyId = Guid.NewGuid();
        var malformedReplacementId = Guid.NewGuid();
        var activeSession = new RefreshTokenSession(
            Guid.NewGuid(),
            administratorId,
            activeFamilyId,
            CreateHash(97),
            now.AddMinutes(-2),
            now.AddDays(7),
            "203.0.113.24");
        var malformedRoot = new RefreshTokenSession(
            Guid.NewGuid(),
            administratorId,
            malformedFamilyId,
            CreateHash(129),
            now.AddMinutes(-5),
            now.AddDays(7),
            "203.0.113.25");
        malformedRoot.Rotate(malformedReplacementId, now.AddMinutes(-3), "203.0.113.26");
        var malformedReplacement = new RefreshTokenSession(
            malformedReplacementId,
            otherAdministratorId,
            malformedFamilyId,
            CreateHash(161),
            now.AddMinutes(-3),
            now.AddDays(7),
            "203.0.113.27");
        malformedReplacement.Revoke(
            RefreshTokenRevocationReason.Logout,
            now.AddMinutes(-1),
            "203.0.113.28");

        await using (var seedContext = _fixture.CreateDbContext())
        {
            await seedContext.Administrators.AddRangeAsync(
                new Administrator(
                    administratorId,
                    AdministratorUserName.Create($"state-owner-{suffix}"),
                    "State query owner",
                    "test-password-hash",
                    now.AddHours(-1)),
                new Administrator(
                    otherAdministratorId,
                    AdministratorUserName.Create($"state-other-{suffix}"),
                    "State query other owner",
                    "test-password-hash",
                    now.AddHours(-1)));
            await seedContext.RefreshTokenSessions.AddRangeAsync(
                activeSession,
                malformedRoot,
                malformedReplacement);
            await seedContext.SaveChangesAsync();
        }

        await using var dbContext = _fixture.CreateDbContext();
        var repository = new AdminAccessManagementRepository(
            dbContext,
            new RefreshTokenFamilyPolicy());

        var exception = await Assert.ThrowsAsync<AdminAccessManagementDomainException>(() =>
            repository.GetSessionFamilyPageAsync(
                administratorId,
                AdminSessionFamilyStateSnapshot.Active,
                now,
                skipCount: 0,
                maxResultCount: 10));

        Assert.Equal(AdminAccessManagementErrorCodes.InvariantViolation, exception.Code);
    }

    private static byte[] CreateHash(byte seed)
    {
        return Enumerable.Range(0, 32).Select(index => unchecked((byte)(seed + index))).ToArray();
    }
}
