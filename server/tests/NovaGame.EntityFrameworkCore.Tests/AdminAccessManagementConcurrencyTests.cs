using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using NovaGame.Application.Contracts.Operations.AdminAccess;
using NovaGame.Domain.Operations.AdminAccess;
using NovaGame.Domain.Shared.Operations.AdminAccess;
using NovaGame.Host.EntityFrameworkCore.Operations.AdminAccess;
using Xunit;

namespace NovaGame.EntityFrameworkCore.Tests;

[Collection(MySqlDatabaseCollection.Name)]
public sealed class AdminAccessManagementConcurrencyTests
{
    private static readonly Guid TargetAdministratorId =
        Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid RoleId =
        Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid FamilyId =
        Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid IdempotencyKey =
        Guid.Parse("50000000-0000-0000-0000-000000000001");
    private readonly MySqlDatabaseFixture _fixture;

    public AdminAccessManagementConcurrencyTests(MySqlDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AdministratorCreateReplayUsesCanonicalFingerprintAndFirstSecretWins()
    {
        var fixture = new AdminAccessManagementApplicationFixture();
        var first = await fixture.Service.CreateAdministratorAsync(
            CreateAdministratorRequest(
                "operator-one",
                "Operator One",
                "First-password-123!"),
            IdempotencyKey);

        var replay = await fixture.Service.CreateAdministratorAsync(
            CreateAdministratorRequest(
                "  ｏｐｅｒａｔｏｒ-one  ",
                "  Ｏperator One  ",
                "Second-password-456!"),
            IdempotencyKey);

        var conflict = await Assert.ThrowsAsync<AdminAccessManagementException>(() =>
            fixture.Service.CreateAdministratorAsync(
                CreateAdministratorRequest(
                    "operator-one",
                    "Different Display Name",
                    "Third-password-789!"),
                IdempotencyKey));

        Assert.Equal(first.Id, replay.Id);
        Assert.Equal(first.UserName, replay.UserName);
        Assert.Equal(first.DisplayName, replay.DisplayName);
        Assert.Equal(first.Revision, replay.Revision);
        Assert.Equal(first.Roles, replay.Roles);
        Assert.Equal(1, first.Revision);
        Assert.Equal(AdminAccessFailureKind.Conflict, conflict.FailureKind);
        Assert.Equal(AdminAccessManagementErrorCodes.IdempotencyConflict, conflict.Code);
        Assert.Equal(["First-password-123!"], fixture.PasswordHasher.HashedPasswords);
        Assert.Single(fixture.Repository.Administrators);
        Assert.Single(fixture.Repository.Audits);
        Assert.Equal(AdminAccessAuditAction.AdministratorCreated, fixture.Repository.Audits[0].Action);
        Assert.Equal(1, fixture.ReceiptRepository.CommittedCount);
        Assert.Equal(3, fixture.ReceiptRepository.ClaimCount);
        Assert.Equal(2, fixture.UnitOfWork.CompleteCount);
        Assert.Equal(3, fixture.UnitOfWork.DisposeCount);
    }

    [Fact]
    public async Task AdministratorCreateLocksBuiltInRoleBeforeLowerGuidCustomRole()
    {
        var fixture = new AdminAccessManagementApplicationFixture();
        var customRoleId = Guid.Parse("01000000-0000-0000-0000-000000000001");
        var builtInRoleId = Guid.Parse("f0000000-0000-0000-0000-000000000001");
        fixture.Repository.SeedRole(new AdminRole(
            customRoleId,
            "Operators",
            AdminAccessManagementApplicationFixture.NowUtc));
        fixture.Repository.SeedRole(new AdminRole(
            builtInRoleId,
            AdminAccessManagementPolicy.BuiltInRoleName,
            AdminAccessManagementApplicationFixture.NowUtc,
            isBuiltIn: true));

        await fixture.Service.CreateAdministratorAsync(
            new CreateAdministratorRequest
            {
                UserName = "lock-order-operator",
                DisplayName = "Lock Order Operator",
                InitialPassword = "Lock-order-password-123!",
                RoleIds = [customRoleId, builtInRoleId]
            },
            Guid.Parse("50000000-0000-0000-0000-000000000002"));

        Assert.Equal([builtInRoleId, customRoleId], fixture.Repository.RoleLockOrder);
    }

    [Fact]
    public async Task RoleCreateReplayCanonicalizesOrderAndRejectsDifferentNonSecretIntent()
    {
        var fixture = new AdminAccessManagementApplicationFixture();
        var first = await fixture.Service.CreateAdminRoleAsync(
            new CreateAdminRoleRequest
            {
                Name = "Operators",
                PermissionNames =
                [
                    AdminAccessPermissions.RolesRead,
                    AdminAccessPermissions.Access
                ]
            },
            IdempotencyKey);

        var replay = await fixture.Service.CreateAdminRoleAsync(
            new CreateAdminRoleRequest
            {
                Name = "  Ｏperators  ",
                PermissionNames =
                [
                    AdminAccessPermissions.Access,
                    AdminAccessPermissions.RolesRead
                ]
            },
            IdempotencyKey);

        var conflict = await Assert.ThrowsAsync<AdminAccessManagementException>(() =>
            fixture.Service.CreateAdminRoleAsync(
                new CreateAdminRoleRequest
                {
                    Name = "Operators",
                    PermissionNames = [AdminAccessPermissions.Access]
                },
                IdempotencyKey));

        Assert.Equal(first.Id, replay.Id);
        Assert.Equal(first.Name, replay.Name);
        Assert.Equal(first.Revision, replay.Revision);
        Assert.Equal(first.PermissionNames, replay.PermissionNames);
        Assert.Equal(
            [AdminAccessPermissions.Access, AdminAccessPermissions.RolesRead],
            replay.PermissionNames);
        Assert.Equal(AdminAccessManagementErrorCodes.IdempotencyConflict, conflict.Code);
        Assert.Single(fixture.Repository.Roles);
        Assert.Equal(3, fixture.Repository.Audits.Count);
        Assert.Equal(1, fixture.ReceiptRepository.CommittedCount);
        Assert.Empty(fixture.PasswordHasher.HashedPasswords);
    }

    [Fact]
    public async Task PasswordResetReplayPrecedesRevisionAndCleansSessionsOnlyOnce()
    {
        var fixture = new AdminAccessManagementApplicationFixture();
        var administrator = CreateTargetAdministrator();
        var session = CreateSession(TargetAdministratorId, FamilyId);
        fixture.Repository.SeedAdministrator(administrator);
        fixture.Repository.SeedSession(session);

        var first = await fixture.Service.ResetAdministratorPasswordAsync(
            TargetAdministratorId,
            expectedRevision: 1,
            new ResetAdministratorPasswordRequest { NewPassword = "First-reset-password-123!" },
            IdempotencyKey);

        var replay = await fixture.Service.ResetAdministratorPasswordAsync(
            TargetAdministratorId,
            expectedRevision: 1,
            new ResetAdministratorPasswordRequest { NewPassword = "Second-reset-password-456!" },
            IdempotencyKey);

        var conflict = await Assert.ThrowsAsync<AdminAccessManagementException>(() =>
            fixture.Service.ResetAdministratorPasswordAsync(
                TargetAdministratorId,
                expectedRevision: 2,
                new ResetAdministratorPasswordRequest { NewPassword = "Third-reset-password-789!" },
                IdempotencyKey));

        Assert.Equal(2, first.Revision);
        Assert.Equal(first, replay);
        Assert.Equal("hashed::First-reset-password-123!", administrator.PasswordHash);
        Assert.Equal(["First-reset-password-123!"], fixture.PasswordHasher.HashedPasswords);
        Assert.True(session.IsRevoked);
        Assert.Equal(RefreshTokenRevocationReason.PasswordReset, session.RevocationReason);
        Assert.Equal(AdminAccessManagementErrorCodes.IdempotencyConflict, conflict.Code);
        Assert.Equal(
            1,
            fixture.Repository.Audits.Count(audit =>
                audit.Action == AdminAccessAuditAction.AdministratorPasswordReset));
        Assert.Equal(
            1,
            fixture.Repository.Audits.Count(audit =>
                audit.Action == AdminAccessAuditAction.SessionFamilyRevoked &&
                audit.SessionFamilyId == FamilyId));
        AssertSubsequence(
            fixture.Repository.Calls,
            "lock-administrator",
            "probe-administrator-session-families",
            "lock-family",
            "add-audit");
    }

    [Fact]
    public async Task PasswordResetRejectsCrossOwnerFamilyBeforeHashMutationAuditOrReceiptCommit()
    {
        var fixture = new AdminAccessManagementApplicationFixture();
        var otherAdministratorId = Guid.Parse("20000000-0000-0000-0000-000000000002");
        var administrator = CreateTargetAdministrator();
        var familyRoot = CreateSession(
            TargetAdministratorId,
            FamilyId,
            Guid.Parse("70000000-0000-0000-0000-000000000010"),
            tokenSeed: 1,
            hoursAgo: 2);
        var familyTerminal = CreateSession(
            otherAdministratorId,
            FamilyId,
            Guid.Parse("70000000-0000-0000-0000-000000000011"),
            tokenSeed: 33,
            hoursAgo: 1);
        familyRoot.Rotate(
            familyTerminal.Id,
            AdminAccessManagementApplicationFixture.NowUtc.AddMinutes(-30),
            "127.0.0.3");
        fixture.Repository.SeedAdministrator(administrator);
        fixture.Repository.SeedSession(familyRoot);
        fixture.Repository.SeedSession(familyTerminal);

        var exception = await Assert.ThrowsAsync<AdminAccessManagementException>(() =>
            fixture.Service.ResetAdministratorPasswordAsync(
                TargetAdministratorId,
                expectedRevision: 1,
                new ResetAdministratorPasswordRequest { NewPassword = "Rejected-reset-password-123!" },
                Guid.Parse("50000000-0000-0000-0000-000000000003")));

        Assert.Equal(AdminAccessFailureKind.InternalError, exception.FailureKind);
        Assert.Equal(AdminAccessManagementErrorCodes.InvariantViolation, exception.Code);
        Assert.Equal("hashed::Original-password-123!", administrator.PasswordHash);
        Assert.Equal(1, administrator.Revision);
        Assert.True(familyRoot.IsRevoked);
        Assert.Equal(RefreshTokenRevocationReason.Rotated, familyRoot.RevocationReason);
        Assert.Equal(familyTerminal.Id, familyRoot.ReplacedBySessionId);
        Assert.False(familyTerminal.IsRevoked);
        Assert.Empty(fixture.PasswordHasher.HashedPasswords);
        Assert.Empty(fixture.Repository.Audits);
        Assert.Equal([FamilyId], fixture.Repository.SessionFamilyLockOrder);
        Assert.Equal(0, fixture.ReceiptRepository.CommittedCount);
        Assert.Equal(1, fixture.ReceiptRepository.ClaimCount);
        Assert.Equal(0, fixture.UnitOfWork.CompleteCount);
        Assert.Equal(1, fixture.UnitOfWork.DisposeCount);
        AssertSubsequence(
            fixture.Repository.Calls,
            "lock-administrator",
            "probe-administrator-session-families",
            "lock-family");
        Assert.DoesNotContain("add-audit", fixture.Repository.Calls);
    }

    [Fact]
    public async Task DeactivationValidatesEveryCompleteFamilyBeforeStateAuditOrRevocation()
    {
        var fixture = new AdminAccessManagementApplicationFixture();
        var builtInRoleId = Guid.Parse("f0000000-0000-0000-0000-000000000002");
        var otherAdministratorId = Guid.Parse("20000000-0000-0000-0000-000000000002");
        var validFamilyId = Guid.Parse("40000000-0000-0000-0000-000000000001");
        var malformedFamilyId = Guid.Parse("40000000-0000-0000-0000-000000000002");
        var builtInRole = new AdminRole(
            builtInRoleId,
            AdminAccessManagementPolicy.BuiltInRoleName,
            AdminAccessManagementApplicationFixture.NowUtc.AddDays(-2),
            isBuiltIn: true);
        var actor = CreateAdministrator(
            AdminAccessManagementApplicationFixture.ActorId,
            "actor",
            "Actor Administrator",
            new AdministratorRoleTarget(
                Guid.Parse("80000000-0000-0000-0000-000000000001"),
                builtInRoleId));
        var target = CreateAdministrator(
            TargetAdministratorId,
            "target-operator",
            "Target Operator",
            new AdministratorRoleTarget(
                Guid.Parse("80000000-0000-0000-0000-000000000002"),
                builtInRoleId));
        var validSession = CreateSession(
            TargetAdministratorId,
            validFamilyId,
            Guid.Parse("70000000-0000-0000-0000-000000000020"),
            tokenSeed: 1,
            hoursAgo: 3);
        var malformedRoot = CreateSession(
            TargetAdministratorId,
            malformedFamilyId,
            Guid.Parse("70000000-0000-0000-0000-000000000021"),
            tokenSeed: 33,
            hoursAgo: 2);
        var malformedTerminal = CreateSession(
            otherAdministratorId,
            malformedFamilyId,
            Guid.Parse("70000000-0000-0000-0000-000000000022"),
            tokenSeed: 65,
            hoursAgo: 1);
        malformedRoot.Rotate(
            malformedTerminal.Id,
            AdminAccessManagementApplicationFixture.NowUtc.AddMinutes(-30),
            "127.0.0.4");
        fixture.Repository.SeedRole(builtInRole);
        fixture.Repository.SeedAdministrator(actor);
        fixture.Repository.SeedAdministrator(target);
        fixture.Repository.SeedSession(validSession);
        fixture.Repository.SeedSession(malformedRoot);
        fixture.Repository.SeedSession(malformedTerminal);

        var exception = await Assert.ThrowsAsync<AdminAccessManagementException>(() =>
            fixture.Service.SetAdministratorActiveStateAsync(
                TargetAdministratorId,
                expectedRevision: 1,
                new SetAdministratorActiveStateRequest { IsActive = false }));

        Assert.Equal(AdminAccessFailureKind.InternalError, exception.FailureKind);
        Assert.Equal(AdminAccessManagementErrorCodes.InvariantViolation, exception.Code);
        Assert.True(target.IsActive);
        Assert.Equal(1, target.Revision);
        Assert.Null(target.UpdatedAtUtc);
        Assert.False(validSession.IsRevoked);
        Assert.True(malformedRoot.IsRevoked);
        Assert.Equal(RefreshTokenRevocationReason.Rotated, malformedRoot.RevocationReason);
        Assert.False(malformedTerminal.IsRevoked);
        Assert.Empty(fixture.Repository.Audits);
        Assert.Empty(fixture.PasswordHasher.HashedPasswords);
        Assert.Equal(
            [validFamilyId, malformedFamilyId],
            fixture.Repository.SessionFamilyLockOrder);
        Assert.Equal(0, fixture.UnitOfWork.CompleteCount);
        Assert.Equal(1, fixture.UnitOfWork.DisposeCount);
        AssertSubsequence(
            fixture.Repository.Calls,
            "lock-built-in-role",
            "lock-administrator",
            "lock-memberships",
            "count-active-role-members",
            "probe-administrator-session-families",
            "lock-family",
            "lock-family");
        Assert.DoesNotContain("add-audit", fixture.Repository.Calls);
    }

    [Fact]
    public async Task RoleRenameRejectsNewDifferentOwnerFoundDuringRevalidationWithoutMutationOrAudit()
    {
        var fixture = new AdminAccessManagementApplicationFixture();
        var competingRoleId = Guid.Parse("40000000-0000-0000-0000-000000000010");
        var role = new AdminRole(
            RoleId,
            "Operators",
            AdminAccessManagementApplicationFixture.NowUtc.AddDays(-2));
        var competingRole = new AdminRole(
            competingRoleId,
            "Pending Reviewers",
            AdminAccessManagementApplicationFixture.NowUtc.AddDays(-2));
        fixture.Repository.SeedRole(role);
        fixture.Repository.SeedRole(competingRole);
        fixture.Repository.OnRolesLocked = lockedRoles =>
        {
            Assert.Equal([RoleId], lockedRoles.Select(lockedRole => lockedRole.Id));
            Assert.True(competingRole.Rename(
                expectedRevision: 1,
                "Reviewers",
                AdminAccessManagementApplicationFixture.NowUtc.AddHours(-1)));
        };

        var exception = await Assert.ThrowsAsync<AdminAccessManagementException>(() =>
            fixture.Service.RenameAdminRoleAsync(
                RoleId,
                expectedRevision: 1,
                new RenameAdminRoleRequest { Name = "Reviewers" }));

        Assert.Equal(AdminAccessFailureKind.Conflict, exception.FailureKind);
        Assert.Equal(AdminAccessManagementErrorCodes.RoleNameAlreadyExists, exception.Code);
        Assert.Equal("Operators", role.Name);
        Assert.Equal(1, role.Revision);
        Assert.Null(role.UpdatedAtUtc);
        Assert.Equal("Reviewers", competingRole.Name);
        Assert.Equal(2, competingRole.Revision);
        Assert.Empty(fixture.Repository.Audits);
        Assert.Equal([RoleId], fixture.Repository.RoleLockOrder);
        Assert.Equal(2, fixture.Repository.Calls.Count(call => call == "probe-role-name"));
        Assert.Equal([false, true], fixture.Repository.RoleNameProbeHasAmbientUnitOfWork);
        Assert.Equal(0, fixture.UnitOfWork.CompleteCount);
        Assert.Equal(1, fixture.UnitOfWork.DisposeCount);
        AssertSubsequence(
            fixture.Repository.Calls,
            "probe-role-name",
            "lock-roles",
            "probe-role-name");
        Assert.DoesNotContain("add-audit", fixture.Repository.Calls);
    }

    [Fact]
    public async Task RoleRenameContinuesWhenInitialOwnerReleasesNameAndKeepsGlobalLockOrder()
    {
        var fixture = new AdminAccessManagementApplicationFixture();
        var initialOwnerId = Guid.Parse("20000000-0000-0000-0000-000000000010");
        var role = new AdminRole(
            RoleId,
            "Operators",
            AdminAccessManagementApplicationFixture.NowUtc.AddDays(-2));
        var initialOwner = new AdminRole(
            initialOwnerId,
            "Reviewers",
            AdminAccessManagementApplicationFixture.NowUtc.AddDays(-2));
        fixture.Repository.SeedRole(role);
        fixture.Repository.SeedRole(initialOwner);
        fixture.Repository.OnRolesLocked = lockedRoles =>
        {
            Assert.Equal(
                [initialOwnerId, RoleId],
                lockedRoles.Select(lockedRole => lockedRole.Id));
            Assert.True(initialOwner.Rename(
                expectedRevision: 1,
                "Former Reviewers",
                AdminAccessManagementApplicationFixture.NowUtc.AddHours(-1)));
        };

        var result = await fixture.Service.RenameAdminRoleAsync(
            RoleId,
            expectedRevision: 1,
            new RenameAdminRoleRequest { Name = "Reviewers" });

        Assert.Equal("Reviewers", result.Name);
        Assert.Equal(2, result.Revision);
        Assert.Equal("Reviewers", role.Name);
        Assert.Equal(2, role.Revision);
        Assert.Equal("Former Reviewers", initialOwner.Name);
        Assert.Equal(2, initialOwner.Revision);
        Assert.Equal([initialOwnerId, RoleId], fixture.Repository.RoleLockOrder);
        Assert.Equal(2, fixture.Repository.Calls.Count(call => call == "probe-role-name"));
        Assert.Equal([false, true], fixture.Repository.RoleNameProbeHasAmbientUnitOfWork);
        var audit = Assert.Single(fixture.Repository.Audits);
        Assert.Equal(AdminAccessAuditAction.RoleRenamed, audit.Action);
        Assert.Equal(RoleId, audit.RoleId);
        Assert.Equal(2, audit.TargetRevision);
        Assert.Equal(1, fixture.UnitOfWork.CompleteCount);
        Assert.Equal(1, fixture.UnitOfWork.DisposeCount);
        AssertSubsequence(
            fixture.Repository.Calls,
            "probe-role-name",
            "lock-roles",
            "probe-role-name",
            "add-audit");
    }

    [Fact]
    public async Task CanonicalRoleNoOpIgnoresStaleRevisionWhileChangedStateConflicts()
    {
        var fixture = new AdminAccessManagementApplicationFixture();
        var originalUpdatedAtUtc = AdminAccessManagementApplicationFixture.NowUtc.AddHours(-1);
        var role = new AdminRole(
            RoleId,
            "Initial Operators",
            AdminAccessManagementApplicationFixture.NowUtc.AddDays(-1));
        Assert.True(role.Rename(
            expectedRevision: 1,
            "Operators",
            originalUpdatedAtUtc));
        fixture.Repository.SeedRole(role);

        var noOp = await fixture.Service.RenameAdminRoleAsync(
            RoleId,
            expectedRevision: 1,
            new RenameAdminRoleRequest { Name = "  Ｏperators  " });

        var conflict = await Assert.ThrowsAsync<AdminAccessManagementException>(() =>
            fixture.Service.RenameAdminRoleAsync(
                RoleId,
                expectedRevision: 1,
                new RenameAdminRoleRequest { Name = "Reviewers" }));

        Assert.Equal(2, noOp.Revision);
        Assert.Equal("Operators", role.Name);
        Assert.Equal(2, role.Revision);
        Assert.Equal(originalUpdatedAtUtc, role.UpdatedAtUtc);
        Assert.Equal(AdminAccessManagementErrorCodes.RevisionConflict, conflict.Code);
        Assert.Empty(fixture.Repository.Audits);
        Assert.Equal(2, fixture.UnitOfWork.BeginCount);
        Assert.Equal(1, fixture.UnitOfWork.CompleteCount);
        Assert.Equal(2, fixture.UnitOfWork.DisposeCount);
    }

    [Fact]
    public async Task PermissionReplacementLocksRoleBeforeChildrenAndAuditsExactDelta()
    {
        var fixture = new AdminAccessManagementApplicationFixture();
        var role = new AdminAccessManagementPolicy().CreateCustomRole(
            RoleId,
            "Operators",
            [AdminAccessPermissions.Access],
            [AdminAccessPermissions.Access],
            AdminAccessManagementApplicationFixture.ActorId,
            AdminAccessManagementApplicationFixture.NowUtc,
            () => Guid.Parse("60000000-0000-0000-0000-000000000001"));
        fixture.Repository.SeedRole(role);

        var result = await fixture.Service.ReplaceAdminRolePermissionsAsync(
            RoleId,
            expectedRevision: 1,
            new ReplaceAdminRolePermissionsRequest
            {
                PermissionNames =
                [
                    AdminAccessPermissions.RolesRead,
                    AdminAccessPermissions.Access
                ]
            });

        Assert.Equal(2, result.Revision);
        Assert.Equal(
            [AdminAccessPermissions.Access, AdminAccessPermissions.RolesRead],
            result.PermissionNames);
        AssertSubsequence(
            fixture.Repository.Calls,
            "lock-role",
            "lock-role-permissions",
            "add-audit");
        var audit = Assert.Single(fixture.Repository.Audits);
        Assert.Equal(AdminAccessAuditAction.PermissionGranted, audit.Action);
        Assert.Equal(AdminAccessPermissions.RolesRead, audit.PermissionName);
        Assert.Equal(2, audit.TargetRevision);
    }

    [Fact]
    public async Task AuditFailureRollsBackNewStateReceiptAndAuditThenReleasesTheKeyForRetry()
    {
        var fixture = new AdminAccessManagementApplicationFixture();
        fixture.Repository.AuditFailure = new InvalidOperationException("injected audit failure");
        var request = CreateAdministratorRequest(
            "rollback-operator",
            "Rollback Operator",
            "Rollback-password-123!");

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.CreateAdministratorAsync(request, IdempotencyKey));

        Assert.Equal("injected audit failure", failure.Message);
        Assert.Empty(fixture.Repository.Administrators);
        Assert.Empty(fixture.Repository.Audits);
        Assert.Equal(0, fixture.ReceiptRepository.CommittedCount);
        Assert.Equal(0, fixture.UnitOfWork.CompleteCount);
        Assert.Equal(1, fixture.UnitOfWork.DisposeCount);

        fixture.Repository.AuditFailure = null;
        var retry = await fixture.Service.CreateAdministratorAsync(request, IdempotencyKey);

        Assert.Equal(1, retry.Revision);
        Assert.Single(fixture.Repository.Administrators);
        Assert.Single(fixture.Repository.Audits);
        Assert.Equal(1, fixture.ReceiptRepository.CommittedCount);
        Assert.Equal(1, fixture.UnitOfWork.CompleteCount);
        Assert.Equal(2, fixture.UnitOfWork.DisposeCount);
        Assert.Equal(2, fixture.PasswordHasher.HashedPasswords.Count);
    }

    [Fact]
    public async Task SelfProtectionRejectsNoOpStateBeforeMutationAuditReceiptOrPasswordHashing()
    {
        var fixture = new AdminAccessManagementApplicationFixture();
        var builtInRoleId = Guid.Parse("31000000-0000-0000-0000-000000000001");
        fixture.Repository.SeedRole(new AdminRole(
            builtInRoleId,
            AdminAccessManagementPolicy.BuiltInRoleName,
            AdminAccessManagementApplicationFixture.NowUtc,
            isBuiltIn: true));
        var actor = new Administrator(
            AdminAccessManagementApplicationFixture.ActorId,
            AdministratorUserName.Create("actor"),
            "Actor",
            "hashed::Actor-password-123!",
            AdminAccessManagementApplicationFixture.NowUtc.AddDays(-1),
            [new AdministratorRoleTarget(
                Guid.Parse("32000000-0000-0000-0000-000000000001"),
                builtInRoleId)],
            AdminAccessManagementApplicationFixture.ActorId,
            isActive: false);
        fixture.Repository.SeedAdministrator(actor);

        var deactivation = await Assert.ThrowsAsync<AdminAccessManagementException>(() =>
            fixture.Service.SetAdministratorActiveStateAsync(
                actor.Id,
                expectedRevision: 1,
                new SetAdministratorActiveStateRequest { IsActive = false }));
        var roleReplacement = await Assert.ThrowsAsync<AdminAccessManagementException>(() =>
            fixture.Service.ReplaceAdministratorRolesAsync(
                actor.Id,
                expectedRevision: 1,
                new ReplaceAdministratorRolesRequest { RoleIds = [builtInRoleId] }));
        var passwordReset = await Assert.ThrowsAsync<AdminAccessManagementException>(() =>
            fixture.Service.ResetAdministratorPasswordAsync(
                actor.Id,
                expectedRevision: 1,
                new ResetAdministratorPasswordRequest { NewPassword = "Actor-reset-password-123!" },
                Guid.Parse("33000000-0000-0000-0000-000000000001")));

        Assert.Equal(AdminAccessManagementErrorCodes.SelfDeactivationForbidden, deactivation.Code);
        Assert.Equal(AdminAccessManagementErrorCodes.SelfRoleMutationForbidden, roleReplacement.Code);
        Assert.Equal(AdminAccessManagementErrorCodes.SelfPasswordResetForbidden, passwordReset.Code);
        Assert.False(actor.IsActive);
        Assert.Equal(1, actor.Revision);
        Assert.Equal([builtInRoleId], actor.RoleMemberships.Select(membership => membership.RoleId));
        Assert.Empty(fixture.Repository.Audits);
        Assert.Equal(0, fixture.ReceiptRepository.CommittedCount);
        Assert.Empty(fixture.PasswordHasher.HashedPasswords);
    }

    [Fact]
    public async Task BuiltInRoleManagementCommandsRejectEvenApparentNoOpsWithoutAudit()
    {
        var fixture = new AdminAccessManagementApplicationFixture();
        var builtInRole = new AdminRole(
            RoleId,
            AdminAccessManagementPolicy.BuiltInRoleName,
            AdminAccessManagementApplicationFixture.NowUtc,
            isBuiltIn: true);
        fixture.Repository.SeedRole(builtInRole);

        var rename = await Assert.ThrowsAsync<AdminAccessManagementException>(() =>
            fixture.Service.RenameAdminRoleAsync(
                builtInRole.Id,
                expectedRevision: 1,
                new RenameAdminRoleRequest { Name = AdminAccessManagementPolicy.BuiltInRoleName }));
        var activeState = await Assert.ThrowsAsync<AdminAccessManagementException>(() =>
            fixture.Service.SetAdminRoleActiveStateAsync(
                builtInRole.Id,
                expectedRevision: 1,
                new SetAdminRoleActiveStateRequest { IsActive = true }));
        var permissions = await Assert.ThrowsAsync<AdminAccessManagementException>(() =>
            fixture.Service.ReplaceAdminRolePermissionsAsync(
                builtInRole.Id,
                expectedRevision: 1,
                new ReplaceAdminRolePermissionsRequest { PermissionNames = [] }));

        Assert.Equal(AdminAccessManagementErrorCodes.BuiltInRoleImmutable, rename.Code);
        Assert.Equal(AdminAccessManagementErrorCodes.BuiltInRoleImmutable, activeState.Code);
        Assert.Equal(AdminAccessManagementErrorCodes.BuiltInRoleImmutable, permissions.Code);
        Assert.Equal(1, builtInRole.Revision);
        Assert.True(builtInRole.IsActive);
        Assert.Empty(fixture.Repository.Audits);
        Assert.Equal(0, fixture.UnitOfWork.CompleteCount);
    }

    [Fact]
    public async Task AuditFailureRollsBackExistingRoleStateWithoutPublishingAnAudit()
    {
        var fixture = new AdminAccessManagementApplicationFixture();
        var role = new AdminRole(RoleId, "Operators", AdminAccessManagementApplicationFixture.NowUtc);
        fixture.Repository.SeedRole(role);
        fixture.Repository.UseDetachedRoleUpdates = true;
        fixture.Repository.AuditFailure = new InvalidOperationException("injected existing-role audit failure");

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.SetAdminRoleActiveStateAsync(
                role.Id,
                expectedRevision: 1,
                new SetAdminRoleActiveStateRequest { IsActive = false }));

        var persistedRole = fixture.Repository.Roles[role.Id];
        Assert.Equal("injected existing-role audit failure", failure.Message);
        Assert.True(persistedRole.IsActive);
        Assert.Equal(1, persistedRole.Revision);
        Assert.Null(persistedRole.UpdatedAtUtc);
        Assert.Empty(fixture.Repository.Audits);
        Assert.Equal(0, fixture.UnitOfWork.CompleteAttemptCount);
        Assert.Equal(0, fixture.UnitOfWork.CompleteCount);
        Assert.Equal(1, fixture.UnitOfWork.DisposeCount);
    }

    [Fact]
    public async Task CommitFailureRollsBackExistingRoleStateAndQueuedAuditThenPermitsRetry()
    {
        var fixture = new AdminAccessManagementApplicationFixture();
        var role = new AdminRole(RoleId, "Operators", AdminAccessManagementApplicationFixture.NowUtc);
        fixture.Repository.SeedRole(role);
        fixture.Repository.UseDetachedRoleUpdates = true;
        fixture.UnitOfWork.CompleteFailure = new InvalidOperationException("injected commit failure");
        var request = new SetAdminRoleActiveStateRequest { IsActive = false };

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.SetAdminRoleActiveStateAsync(role.Id, expectedRevision: 1, request));

        var persistedRole = fixture.Repository.Roles[role.Id];
        Assert.Equal("injected commit failure", failure.Message);
        Assert.True(persistedRole.IsActive);
        Assert.Equal(1, persistedRole.Revision);
        Assert.Null(persistedRole.UpdatedAtUtc);
        Assert.Empty(fixture.Repository.Audits);
        Assert.Equal(1, fixture.UnitOfWork.CompleteAttemptCount);
        Assert.Equal(0, fixture.UnitOfWork.CompleteCount);
        Assert.Equal(1, fixture.UnitOfWork.DisposeCount);

        fixture.UnitOfWork.CompleteFailure = null;
        var retry = await fixture.Service.SetAdminRoleActiveStateAsync(
            role.Id,
            expectedRevision: 1,
            request);

        Assert.False(retry.IsActive);
        Assert.Equal(2, retry.Revision);
        persistedRole = fixture.Repository.Roles[role.Id];
        Assert.False(persistedRole.IsActive);
        Assert.Equal(2, persistedRole.Revision);
        Assert.Single(fixture.Repository.Audits);
        Assert.Equal(AdminAccessAuditAction.RoleDeactivated, fixture.Repository.Audits[0].Action);
        Assert.Equal(2, fixture.UnitOfWork.CompleteAttemptCount);
        Assert.Equal(1, fixture.UnitOfWork.CompleteCount);
        Assert.Equal(2, fixture.UnitOfWork.DisposeCount);
    }

    [Fact]
    public async Task ConcurrentMatchingCreateCommandsSerializeToOneMutationAndOneReplay()
    {
        var fixture = new AdminAccessManagementApplicationFixture();
        var firstMutationEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstMutation = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var addCallCount = 0;
        fixture.Repository.OnAddAdministratorAsync = async (_, cancellationToken) =>
        {
            if (Interlocked.Increment(ref addCallCount) != 1)
            {
                return;
            }

            firstMutationEntered.TrySetResult();
            await releaseFirstMutation.Task.WaitAsync(cancellationToken);
        };

        var firstTask = fixture.Service.CreateAdministratorAsync(
            CreateAdministratorRequest(
                "concurrent-operator",
                "Concurrent Operator",
                "First-concurrent-password-123!"),
            IdempotencyKey);
        await firstMutationEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var secondTask = fixture.Service.CreateAdministratorAsync(
            CreateAdministratorRequest(
                "concurrent-operator",
                "Concurrent Operator",
                "Second-concurrent-password-456!"),
            IdempotencyKey);
        await fixture.ReceiptRepository.SecondAttempted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, Volatile.Read(ref addCallCount));
        Assert.Single(fixture.PasswordHasher.HashedPasswords);
        releaseFirstMutation.TrySetResult();

        var results = await Task.WhenAll(firstTask, secondTask)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(results[0], results[1]);
        Assert.Equal(1, Volatile.Read(ref addCallCount));
        Assert.Single(fixture.Repository.Administrators);
        Assert.Single(fixture.Repository.Audits);
        Assert.Single(fixture.PasswordHasher.HashedPasswords);
        Assert.Equal("First-concurrent-password-123!", fixture.PasswordHasher.HashedPasswords[0]);
        Assert.Equal(1, fixture.ReceiptRepository.CommittedCount);
        Assert.Equal(2, fixture.UnitOfWork.CompleteCount);
        Assert.Equal(2, fixture.UnitOfWork.DisposeCount);
    }

    [Fact]
    public async Task MySqlReceiptClaimBlocksOnTheUniqueKeyAndRollbackReleasesItForRetry()
    {
        var actorId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid();
        var requestFingerprint = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        var firstCandidate = new AdminAccessCommandReceipt(
            Guid.NewGuid(),
            actorId,
            AdminAccessCommandKind.AdminRoleCreate,
            idempotencyKey,
            requestFingerprint,
            Guid.NewGuid(),
            resultingRevision: 1,
            DateTime.UtcNow);

        await using var firstContext = _fixture.CreateDbContext();
        await using var firstTransaction = await firstContext.Database.BeginTransactionAsync();
        var firstRepository = new AdminAccessCommandReceiptRepository(firstContext);
        var firstClaim = await firstRepository.ClaimAsync(firstCandidate);
        Assert.Equal(firstCandidate.Id, firstClaim.Id);

        var competingCandidate = new AdminAccessCommandReceipt(
            Guid.NewGuid(),
            actorId,
            AdminAccessCommandKind.AdminRoleCreate,
            idempotencyKey,
            requestFingerprint,
            Guid.NewGuid(),
            resultingRevision: 1,
            DateTime.UtcNow);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var competingFailure = await Task.Run(
            () => ClaimWithOneSecondLockWaitAsync(competingCandidate, cancellation.Token),
            cancellation.Token).WaitAsync(cancellation.Token);

        Assert.NotNull(competingFailure);
        Assert.True(ContainsMySqlLockWaitTimeout(competingFailure));
        await firstTransaction.RollbackAsync(cancellation.Token);

        var retryCandidate = new AdminAccessCommandReceipt(
            Guid.NewGuid(),
            actorId,
            AdminAccessCommandKind.AdminRoleCreate,
            idempotencyKey,
            requestFingerprint,
            Guid.NewGuid(),
            resultingRevision: 1,
            DateTime.UtcNow);
        await using (var retryContext = _fixture.CreateDbContext())
        {
            await using var retryTransaction = await retryContext.Database.BeginTransactionAsync(cancellation.Token);
            var retryClaim = await new AdminAccessCommandReceiptRepository(retryContext)
                .ClaimAsync(retryCandidate, cancellation.Token);
            Assert.Equal(retryCandidate.Id, retryClaim.Id);
            await retryTransaction.CommitAsync(cancellation.Token);
        }

        await using var verificationContext = _fixture.CreateDbContext();
        var receipts = await verificationContext.AdminAccessCommandReceipts
            .Where(receipt =>
                receipt.ActorAdministratorId == actorId &&
                receipt.CommandKind == AdminAccessCommandKind.AdminRoleCreate &&
                receipt.IdempotencyKey == idempotencyKey)
            .ToArrayAsync(cancellation.Token);
        var persistedReceipt = Assert.Single(receipts);
        Assert.Equal(retryCandidate.Id, persistedReceipt.Id);
        Assert.True(persistedReceipt.MatchesFingerprint(requestFingerprint));
    }

    private async Task<Exception?> ClaimWithOneSecondLockWaitAsync(
        AdminAccessCommandReceipt candidate,
        CancellationToken cancellationToken)
    {
        await using var dbContext = _fixture.CreateDbContext();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "SET SESSION innodb_lock_wait_timeout = 1",
            cancellationToken);
        try
        {
            _ = await new AdminAccessCommandReceiptRepository(dbContext)
                .ClaimAsync(candidate, cancellationToken);
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static bool ContainsMySqlLockWaitTimeout(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is MySqlException { Number: 1205 })
            {
                return true;
            }
        }

        return false;
    }

    private static CreateAdministratorRequest CreateAdministratorRequest(
        string userName,
        string displayName,
        string initialPassword)
    {
        return new CreateAdministratorRequest
        {
            UserName = userName,
            DisplayName = displayName,
            InitialPassword = initialPassword,
            RoleIds = []
        };
    }

    private static Administrator CreateTargetAdministrator()
    {
        return CreateAdministrator(
            TargetAdministratorId,
            "target-operator",
            "Target Operator");
    }

    private static Administrator CreateAdministrator(
        Guid administratorId,
        string userName,
        string displayName,
        params AdministratorRoleTarget[] initialRoles)
    {
        return new Administrator(
            administratorId,
            AdministratorUserName.Create(userName),
            displayName,
            "hashed::Original-password-123!",
            AdminAccessManagementApplicationFixture.NowUtc.AddDays(-1),
            initialRoles);
    }

    private static RefreshTokenSession CreateSession(Guid administratorId, Guid familyId)
    {
        return CreateSession(
            administratorId,
            familyId,
            Guid.Parse("70000000-0000-0000-0000-000000000001"),
            tokenSeed: 1,
            hoursAgo: 1);
    }

    private static RefreshTokenSession CreateSession(
        Guid administratorId,
        Guid familyId,
        Guid sessionId,
        int tokenSeed,
        int hoursAgo)
    {
        var tokenHash = Enumerable.Range(tokenSeed, 32).Select(value => (byte)value).ToArray();
        return new RefreshTokenSession(
            sessionId,
            administratorId,
            familyId,
            tokenHash,
            AdminAccessManagementApplicationFixture.NowUtc.AddHours(-hoursAgo),
            AdminAccessManagementApplicationFixture.NowUtc.AddDays(1),
            "127.0.0.2");
    }

    private static void AssertSubsequence(
        IReadOnlyList<string> calls,
        params string[] expected)
    {
        var nextIndex = 0;
        foreach (var call in calls)
        {
            if (nextIndex < expected.Length &&
                string.Equals(call, expected[nextIndex], StringComparison.Ordinal))
            {
                nextIndex++;
            }
        }

        Assert.Equal(expected.Length, nextIndex);
    }
}
