using NovaGame.Domain.Operations.AdminAccess;
using NovaGame.Domain.Shared.Operations.AdminAccess;
using Xunit;

namespace NovaGame.EntityFrameworkCore.Tests;

public sealed class AdminAccessManagementRoleDomainTests
{
    private static readonly Guid ActorId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TargetId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid RoleId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTime NowUtc = new(2026, 8, 14, 10, 0, 0, DateTimeKind.Utc);

    private const string AccessPermission = "Operations.AdminAccess.Access";
    private const string ReadPermission = "Operations.AdminAccess.Roles.Read";
    private const string ManagePermission = "Operations.AdminAccess.Roles.Manage";

    [Fact]
    public void CustomRoleCreationCanonicalizesNameAndInitialPermissionsAtRevisionOne()
    {
        var policy = new AdminAccessManagementPolicy();
        var grants = NewGuidFactory(
            Guid.Parse("40000000-0000-0000-0000-000000000001"),
            Guid.Parse("40000000-0000-0000-0000-000000000002"));

        var role = policy.CreateCustomRole(
            RoleId,
            "  Ｏperators  ",
            [ManagePermission, AccessPermission],
            [AccessPermission, ReadPermission, ManagePermission],
            ActorId,
            NowUtc,
            grants);

        Assert.Equal("Operators", role.Name);
        Assert.Equal("OPERATORS", role.NormalizedName);
        Assert.False(role.IsBuiltIn);
        Assert.True(role.IsActive);
        Assert.Equal(1, role.Revision);
        Assert.Equal(NowUtc, role.CreatedAtUtc);
        Assert.Null(role.UpdatedAtUtc);
        Assert.Equal(
            [AccessPermission, ManagePermission],
            role.Permissions.Select(permission => permission.Name));
        Assert.All(role.Permissions, permission =>
        {
            Assert.Equal(RoleId, permission.RoleId);
            Assert.Equal(NowUtc, permission.GrantedAtUtc);
            Assert.Equal(ActorId, permission.GrantedByAdministratorId);
        });
    }

    [Fact]
    public void CustomRoleNameAndStateSettersAreCanonicalRevisionAwareTargets()
    {
        var role = new AdminRole(RoleId, "Operators", NowUtc);

        Assert.False(role.Rename(0, "  Operators  ", DateTime.MinValue));
        Assert.Equal(1, role.Revision);
        Assert.Null(role.UpdatedAtUtc);

        Assert.True(role.Rename(1, "Reviewers", NowUtc.AddMinutes(1)));
        Assert.Equal("Reviewers", role.Name);
        Assert.Equal("REVIEWERS", role.NormalizedName);
        Assert.Equal(2, role.Revision);
        Assert.Equal(NowUtc.AddMinutes(1), role.UpdatedAtUtc);

        Assert.False(role.Rename(1, "Reviewers", DateTime.MinValue));
        Assert.False(role.SetActiveState(1, isActive: true, DateTime.MinValue));
        Assert.Equal(2, role.Revision);
        Assert.Equal(NowUtc.AddMinutes(1), role.UpdatedAtUtc);

        var staleRename = Assert.Throws<AdminAccessManagementDomainException>(() =>
            role.Rename(1, "Auditors", NowUtc.AddMinutes(2)));
        AssertFailure(
            staleRename,
            AdminAccessManagementErrorCodes.RevisionConflict,
            AdminAccessFailureKind.Conflict);

        Assert.True(role.SetActiveState(2, isActive: false, NowUtc.AddMinutes(2)));
        Assert.False(role.IsActive);
        Assert.Equal(3, role.Revision);
        Assert.Equal(NowUtc.AddMinutes(2), role.UpdatedAtUtc);

        Assert.False(role.SetActiveState(1, isActive: false, DateTime.MinValue));
        Assert.True(role.SetActiveState(3, isActive: true, NowUtc.AddMinutes(3)));
        Assert.True(role.IsActive);
        Assert.Equal(4, role.Revision);
    }

    [Fact]
    public void RoleNamesRejectMalformedTextWithAStableFailure()
    {
        var invalidControl = Assert.Throws<AdminAccessManagementDomainException>(() =>
            new AdminRole(RoleId, "Operators\u0001", NowUtc));
        var invalidLength = Assert.Throws<AdminAccessManagementDomainException>(() =>
            new AdminRole(RoleId, new string('a', 65), NowUtc));
        var malformedBuiltIn = Assert.Throws<AdminAccessManagementDomainException>(() =>
            new AdminRole(RoleId, "Super Administrators", NowUtc, isBuiltIn: true));

        AssertFailure(
            invalidControl,
            AdminAccessManagementErrorCodes.InvalidRequest,
            AdminAccessFailureKind.BadRequest);
        AssertFailure(
            invalidLength,
            AdminAccessManagementErrorCodes.InvalidRequest,
            AdminAccessFailureKind.BadRequest);
        AssertFailure(
            malformedBuiltIn,
            AdminAccessManagementErrorCodes.InvariantViolation,
            AdminAccessFailureKind.InternalError);
    }

    [Fact]
    public void PermissionReplacementPreservesEvidenceAndAdvancesOncePerTargetChange()
    {
        var policy = new AdminAccessManagementPolicy();
        var role = policy.CreateCustomRole(
            RoleId,
            "Operators",
            [AccessPermission, ReadPermission],
            [AccessPermission, ReadPermission, ManagePermission],
            ActorId,
            NowUtc,
            NewGuidFactory(Guid.NewGuid(), Guid.NewGuid()));
        var retained = Assert.Single(role.Permissions, permission => permission.Name == AccessPermission);
        var removedEvidence = Assert.Single(role.Permissions, permission => permission.Name == ReadPermission);
        var addedId = Guid.Parse("50000000-0000-0000-0000-000000000001");

        var changed = policy.ReplaceRolePermissions(
            role,
            expectedRevision: 1,
            [ManagePermission, AccessPermission],
            [AccessPermission, ReadPermission, ManagePermission],
            ActorId,
            NowUtc.AddMinutes(1),
            NewGuidFactory(addedId));

        Assert.True(changed.Changed);
        Assert.Same(removedEvidence, Assert.Single(changed.Removed));
        var added = Assert.Single(changed.Added);
        Assert.Equal(addedId, added.Id);
        Assert.Equal(ManagePermission, added.Name);
        Assert.Equal(ActorId, added.GrantedByAdministratorId);
        Assert.Equal(NowUtc.AddMinutes(1), added.GrantedAtUtc);
        Assert.Same(
            retained,
            Assert.Single(role.Permissions, permission => permission.Name == AccessPermission));
        Assert.Equal(
            [AccessPermission, ManagePermission],
            role.Permissions.Select(permission => permission.Name));
        Assert.Equal(2, role.Revision);
        Assert.Equal(NowUtc.AddMinutes(1), role.UpdatedAtUtc);

        var noOp = policy.ReplaceRolePermissions(
            role,
            expectedRevision: 1,
            [AccessPermission, ManagePermission],
            [AccessPermission, ManagePermission],
            ActorId,
            DateTime.MinValue,
            () => throw new InvalidOperationException("A no-op must not request a grant ID."));
        Assert.False(noOp.Changed);
        Assert.Empty(noOp.Added);
        Assert.Empty(noOp.Removed);
        Assert.Equal(2, role.Revision);

        var staleChange = Assert.Throws<AdminAccessManagementDomainException>(() =>
            policy.ReplaceRolePermissions(
                role,
                expectedRevision: 1,
                [AccessPermission],
                [AccessPermission, ManagePermission],
                ActorId,
                NowUtc.AddMinutes(2),
                NewGuidFactory()));
        AssertFailure(
            staleChange,
            AdminAccessManagementErrorCodes.RevisionConflict,
            AdminAccessFailureKind.Conflict);
    }

    [Fact]
    public void PermissionReplacementAllowsAnExplicitEmptyTargetAndStaleIdenticalRetry()
    {
        var policy = new AdminAccessManagementPolicy();
        var role = policy.CreateCustomRole(
            RoleId,
            "Operators",
            [AccessPermission, ReadPermission],
            [AccessPermission, ReadPermission],
            ActorId,
            NowUtc,
            NewGuidFactory(Guid.NewGuid(), Guid.NewGuid()));

        var changed = policy.ReplaceRolePermissions(
            role,
            expectedRevision: 1,
            [],
            [AccessPermission, ReadPermission],
            ActorId,
            NowUtc.AddMinutes(1),
            () => throw new InvalidOperationException("An empty target must not request a grant ID."));

        Assert.True(changed.Changed);
        Assert.Empty(changed.Added);
        Assert.Equal(
            [AccessPermission, ReadPermission],
            changed.Removed.Select(permission => permission.Name));
        Assert.Empty(role.Permissions);
        Assert.Equal(2, role.Revision);
        Assert.Equal(NowUtc.AddMinutes(1), role.UpdatedAtUtc);

        var retry = policy.ReplaceRolePermissions(
            role,
            expectedRevision: 1,
            [],
            [AccessPermission, ReadPermission],
            ActorId,
            DateTime.MinValue,
            () => throw new InvalidOperationException("An identical empty target must not request a grant ID."));

        Assert.False(retry.Changed);
        Assert.Empty(retry.Added);
        Assert.Empty(retry.Removed);
        Assert.Empty(role.Permissions);
        Assert.Equal(2, role.Revision);
        Assert.Equal(NowUtc.AddMinutes(1), role.UpdatedAtUtc);
    }

    [Fact]
    public void PermissionReplacementRejectsUnknownDuplicateAndInvalidGrantStateAtomically()
    {
        var policy = new AdminAccessManagementPolicy();
        var role = policy.CreateCustomRole(
            RoleId,
            "Operators",
            [AccessPermission],
            [AccessPermission, ManagePermission],
            ActorId,
            NowUtc,
            NewGuidFactory(Guid.NewGuid()));

        var unknown = Assert.Throws<AdminAccessManagementDomainException>(() =>
            policy.ReplaceRolePermissions(
                role,
                1,
                ["operations.adminaccess.access"],
                [AccessPermission, ManagePermission],
                ActorId,
                NowUtc.AddMinutes(1),
                NewGuidFactory(Guid.NewGuid())));
        var duplicate = Assert.Throws<AdminAccessManagementDomainException>(() =>
            policy.ReplaceRolePermissions(
                role,
                1,
                [AccessPermission, AccessPermission],
                [AccessPermission, ManagePermission],
                ActorId,
                NowUtc.AddMinutes(1),
                NewGuidFactory(Guid.NewGuid())));
        var invalidGrant = Assert.Throws<AdminAccessManagementDomainException>(() =>
            policy.ReplaceRolePermissions(
                role,
                1,
                [AccessPermission, ManagePermission],
                [AccessPermission, ManagePermission],
                ActorId,
                NowUtc.AddMinutes(1),
                NewGuidFactory(Guid.Empty)));

        AssertFailure(
            unknown,
            AdminAccessManagementErrorCodes.UnknownPermission,
            AdminAccessFailureKind.UnprocessableEntity);
        AssertFailure(
            duplicate,
            AdminAccessManagementErrorCodes.InvalidRequest,
            AdminAccessFailureKind.BadRequest);
        AssertFailure(
            invalidGrant,
            AdminAccessManagementErrorCodes.InvalidRequest,
            AdminAccessFailureKind.BadRequest);
        Assert.Equal(1, role.Revision);
        Assert.Null(role.UpdatedAtUtc);
        Assert.Equal(AccessPermission, Assert.Single(role.Permissions).Name);
    }

    [Fact]
    public void MalformedPermissionCatalogFailsBeforeCustomOrBuiltInRoleMutation()
    {
        var policy = new AdminAccessManagementPolicy();
        var customRole = policy.CreateCustomRole(
            RoleId,
            "Operators",
            [AccessPermission],
            [AccessPermission, ReadPermission],
            ActorId,
            NowUtc,
            NewGuidFactory(Guid.NewGuid()));
        var builtInRole = policy.CreateBuiltInRole(
            Guid.NewGuid(),
            [AccessPermission],
            NowUtc,
            NewGuidFactory(Guid.NewGuid()));

        var customFailure = Assert.Throws<AdminAccessManagementDomainException>(() =>
            policy.ReplaceRolePermissions(
                customRole,
                expectedRevision: 1,
                [AccessPermission],
                [AccessPermission, AccessPermission],
                ActorId,
                NowUtc.AddMinutes(1),
                NewGuidFactory()));
        var builtInFailure = Assert.Throws<AdminAccessManagementDomainException>(() =>
            policy.SynchronizeBuiltInRolePermissions(
                builtInRole,
                ["Operations.AdminAccess\u0001"],
                NowUtc.AddMinutes(1),
                NewGuidFactory()));

        AssertFailure(
            customFailure,
            AdminAccessManagementErrorCodes.InvariantViolation,
            AdminAccessFailureKind.InternalError);
        AssertFailure(
            builtInFailure,
            AdminAccessManagementErrorCodes.InvariantViolation,
            AdminAccessFailureKind.InternalError);
        Assert.Equal(1, customRole.Revision);
        Assert.Equal(AccessPermission, Assert.Single(customRole.Permissions).Name);
        Assert.Equal(1, builtInRole.Revision);
        Assert.Equal(AccessPermission, Assert.Single(builtInRole.Permissions).Name);
    }

    [Fact]
    public void BuiltInRoleRejectsOperatorMutationAndSystemSynchronizationIsExactAndIdempotent()
    {
        var policy = new AdminAccessManagementPolicy();
        var retainedId = Guid.Parse("60000000-0000-0000-0000-000000000001");
        var obsoleteId = Guid.Parse("60000000-0000-0000-0000-000000000002");
        var role = policy.CreateBuiltInRole(
            RoleId,
            [AccessPermission, "Operations.Obsolete"],
            NowUtc,
            NewGuidFactory(retainedId, obsoleteId));
        var retained = Assert.Single(role.Permissions, permission => permission.Name == AccessPermission);

        var rename = Assert.Throws<AdminAccessManagementDomainException>(() =>
            role.Rename(0, AdminAccessManagementPolicy.BuiltInRoleName, DateTime.MinValue));
        var state = Assert.Throws<AdminAccessManagementDomainException>(() =>
            role.SetActiveState(0, isActive: true, DateTime.MinValue));
        var replace = Assert.Throws<AdminAccessManagementDomainException>(() =>
            policy.ReplaceRolePermissions(
                role,
                0,
                [AccessPermission, "Operations.Obsolete"],
                [AccessPermission, "Operations.Obsolete"],
                ActorId,
                DateTime.MinValue,
                NewGuidFactory()));
        var invalidReplace = Assert.Throws<AdminAccessManagementDomainException>(() =>
            policy.ReplaceRolePermissions(
                role,
                expectedRevision: 0,
                ["Operations.Unknown"],
                [AccessPermission],
                ActorId,
                DateTime.MinValue,
                NewGuidFactory()));

        AssertFailure(rename, AdminAccessManagementErrorCodes.BuiltInRoleImmutable, AdminAccessFailureKind.UnprocessableEntity);
        AssertFailure(state, AdminAccessManagementErrorCodes.BuiltInRoleImmutable, AdminAccessFailureKind.UnprocessableEntity);
        AssertFailure(replace, AdminAccessManagementErrorCodes.BuiltInRoleImmutable, AdminAccessFailureKind.UnprocessableEntity);
        AssertFailure(invalidReplace, AdminAccessManagementErrorCodes.BuiltInRoleImmutable, AdminAccessFailureKind.UnprocessableEntity);

        var addedId = Guid.Parse("60000000-0000-0000-0000-000000000003");
        var synchronized = policy.SynchronizeBuiltInRolePermissions(
            role,
            [ManagePermission, AccessPermission],
            NowUtc.AddMinutes(1),
            NewGuidFactory(addedId));

        Assert.True(synchronized.Changed);
        Assert.Equal("Operations.Obsolete", Assert.Single(synchronized.Removed).Name);
        Assert.Equal(addedId, Assert.Single(synchronized.Added).Id);
        Assert.Same(
            retained,
            Assert.Single(role.Permissions, permission => permission.Name == AccessPermission));
        Assert.Equal([AccessPermission, ManagePermission], role.Permissions.Select(permission => permission.Name));
        Assert.Equal(2, role.Revision);

        var repeated = policy.SynchronizeBuiltInRolePermissions(
            role,
            [AccessPermission, ManagePermission],
            DateTime.MinValue,
            () => throw new InvalidOperationException("A no-op must not request a grant ID."));
        Assert.False(repeated.Changed);
        Assert.Equal(2, role.Revision);
        Assert.Equal(NowUtc.AddMinutes(1), role.UpdatedAtUtc);
    }

    [Fact]
    public void PolicyRejectsMissingMalformedAndDuplicateBuiltInState()
    {
        var policy = new AdminAccessManagementPolicy();
        var builtIn = policy.CreateBuiltInRole(
            RoleId,
            [AccessPermission],
            NowUtc,
            NewGuidFactory(Guid.NewGuid()));
        var duplicate = policy.CreateBuiltInRole(
            Guid.NewGuid(),
            [AccessPermission],
            NowUtc,
            NewGuidFactory(Guid.NewGuid()));
        var malformed = new AdminRole(Guid.NewGuid(), AdminAccessManagementPolicy.BuiltInRoleName, NowUtc);

        Assert.Same(builtIn, policy.ResolveBuiltInRole([builtIn], administratorsExist: true));
        Assert.Null(policy.ResolveBuiltInRole([], administratorsExist: false));
        AssertFailure(
            Assert.Throws<AdminAccessManagementDomainException>(() =>
                policy.ResolveBuiltInRole([], administratorsExist: true)),
            AdminAccessManagementErrorCodes.InvariantViolation,
            AdminAccessFailureKind.InternalError);
        AssertFailure(
            Assert.Throws<AdminAccessManagementDomainException>(() =>
                policy.ResolveBuiltInRole([builtIn, duplicate], administratorsExist: true)),
            AdminAccessManagementErrorCodes.InvariantViolation,
            AdminAccessFailureKind.InternalError);
        AssertFailure(
            Assert.Throws<AdminAccessManagementDomainException>(() =>
                policy.ResolveBuiltInRole([malformed], administratorsExist: true)),
            AdminAccessManagementErrorCodes.InvariantViolation,
            AdminAccessFailureKind.InternalError);
    }

    [Fact]
    public void PolicyChecksRequestedRoleAndAdministratorLockoutFacts()
    {
        var policy = new AdminAccessManagementPolicy();
        var activeRole = new AdminRole(RoleId, "Operators", NowUtc);
        var inactiveRole = new AdminRole(Guid.NewGuid(), "Reviewers", NowUtc);
        inactiveRole.SetActiveState(1, isActive: false, NowUtc.AddMinutes(1));

        Assert.Equal(
            activeRole,
            Assert.Single(policy.RequireActiveRequestedRoles([RoleId], [activeRole, inactiveRole])));
        AssertFailure(
            Assert.Throws<AdminAccessManagementDomainException>(() =>
                policy.RequireActiveRequestedRoles([Guid.NewGuid()], [activeRole, inactiveRole])),
            AdminAccessManagementErrorCodes.RoleNotFound,
            AdminAccessFailureKind.NotFound);
        AssertFailure(
            Assert.Throws<AdminAccessManagementDomainException>(() =>
                policy.RequireActiveRequestedRoles([inactiveRole.Id], [activeRole, inactiveRole])),
            AdminAccessManagementErrorCodes.RoleInactive,
            AdminAccessFailureKind.UnprocessableEntity);

        AssertFailure(
            Assert.Throws<AdminAccessManagementDomainException>(() =>
                policy.EnsureCanSetAdministratorActiveState(
                    ActorId,
                    ActorId,
                    targetIsCurrentlyActive: false,
                    requestedIsActive: false,
                    targetHasBuiltInRole: true,
                    activeBuiltInAdministratorCount: 1)),
            AdminAccessManagementErrorCodes.SelfDeactivationForbidden,
            AdminAccessFailureKind.UnprocessableEntity);
        AssertFailure(
            Assert.Throws<AdminAccessManagementDomainException>(() =>
                policy.EnsureCanReplaceAdministratorRoles(
                    ActorId,
                    ActorId,
                    targetIsActive: true,
                    targetHasBuiltInRole: true,
                    requestedRolesIncludeBuiltInRole: true,
                    activeBuiltInAdministratorCount: 1)),
            AdminAccessManagementErrorCodes.SelfRoleMutationForbidden,
            AdminAccessFailureKind.UnprocessableEntity);
        AssertFailure(
            Assert.Throws<AdminAccessManagementDomainException>(() =>
                policy.EnsureCanSetAdministratorActiveState(
                    ActorId,
                    TargetId,
                    targetIsCurrentlyActive: true,
                    requestedIsActive: false,
                    targetHasBuiltInRole: true,
                    activeBuiltInAdministratorCount: 1)),
            AdminAccessManagementErrorCodes.LastActiveBuiltInAdministratorRequired,
            AdminAccessFailureKind.UnprocessableEntity);
        AssertFailure(
            Assert.Throws<AdminAccessManagementDomainException>(() =>
                policy.EnsureCanReplaceAdministratorRoles(
                    ActorId,
                    TargetId,
                    targetIsActive: true,
                    targetHasBuiltInRole: true,
                    requestedRolesIncludeBuiltInRole: false,
                    activeBuiltInAdministratorCount: 1)),
            AdminAccessManagementErrorCodes.LastActiveBuiltInAdministratorRequired,
            AdminAccessFailureKind.UnprocessableEntity);
        AssertFailure(
            Assert.Throws<AdminAccessManagementDomainException>(() =>
                policy.EnsureCanSetAdministratorActiveState(
                    ActorId,
                    TargetId,
                    targetIsCurrentlyActive: true,
                    requestedIsActive: false,
                    targetHasBuiltInRole: true,
                    activeBuiltInAdministratorCount: 0)),
            AdminAccessManagementErrorCodes.InvariantViolation,
            AdminAccessFailureKind.InternalError);
        AssertFailure(
            Assert.Throws<AdminAccessManagementDomainException>(() =>
                policy.EnsureCanResetAdministratorPassword(ActorId, ActorId)),
            AdminAccessManagementErrorCodes.SelfPasswordResetForbidden,
            AdminAccessFailureKind.UnprocessableEntity);
    }

    [Fact]
    public void PolicyRejectsMalformedRequestedAndLockedRoleFactsBeforeResolution()
    {
        var policy = new AdminAccessManagementPolicy();
        var role = new AdminRole(RoleId, "Operators", NowUtc);

        var emptyRequestedId = Assert.Throws<AdminAccessManagementDomainException>(() =>
            policy.RequireActiveRequestedRoles([Guid.Empty], [role]));
        var duplicateRequestedId = Assert.Throws<AdminAccessManagementDomainException>(() =>
            policy.RequireActiveRequestedRoles([RoleId, RoleId], [role]));
        var duplicateLockedRole = Assert.Throws<AdminAccessManagementDomainException>(() =>
            policy.RequireActiveRequestedRoles([RoleId], [role, role]));

        AssertFailure(
            emptyRequestedId,
            AdminAccessManagementErrorCodes.InvalidRequest,
            AdminAccessFailureKind.BadRequest);
        AssertFailure(
            duplicateRequestedId,
            AdminAccessManagementErrorCodes.InvalidRequest,
            AdminAccessFailureKind.BadRequest);
        AssertFailure(
            duplicateLockedRole,
            AdminAccessManagementErrorCodes.InvariantViolation,
            AdminAccessFailureKind.InternalError);
    }

    [Fact]
    public void PolicyAllowsSafeNonSelfBuiltInAdministratorChanges()
    {
        var policy = new AdminAccessManagementPolicy();

        var deactivation = Record.Exception(() => policy.EnsureCanSetAdministratorActiveState(
            ActorId,
            TargetId,
            targetIsCurrentlyActive: true,
            requestedIsActive: false,
            targetHasBuiltInRole: true,
            activeBuiltInAdministratorCount: 2));
        var roleReplacement = Record.Exception(() => policy.EnsureCanReplaceAdministratorRoles(
            ActorId,
            TargetId,
            targetIsActive: true,
            targetHasBuiltInRole: true,
            requestedRolesIncludeBuiltInRole: true,
            activeBuiltInAdministratorCount: 1));

        Assert.Null(deactivation);
        Assert.Null(roleReplacement);
    }

    [Fact]
    public void PolicyRejectsAnotherRoleWithTheSameNormalizedName()
    {
        var policy = new AdminAccessManagementPolicy();
        var existingRole = new AdminRole(Guid.NewGuid(), "Operators", NowUtc);

        policy.EnsureRoleNameAvailable(existingRole, existingRole.Id);

        var duplicate = Assert.Throws<AdminAccessManagementDomainException>(() =>
            policy.EnsureRoleNameAvailable(existingRole, RoleId));

        AssertFailure(
            duplicate,
            AdminAccessManagementErrorCodes.RoleNameAlreadyExists,
            AdminAccessFailureKind.Conflict);
    }

    private static Func<Guid> NewGuidFactory(params Guid[] identifiers)
    {
        var remaining = new Queue<Guid>(identifiers);
        return () => remaining.Dequeue();
    }

    private static void AssertFailure(
        AdminAccessManagementDomainException exception,
        string code,
        AdminAccessFailureKind failureKind)
    {
        Assert.Equal(code, exception.Code);
        Assert.Equal(failureKind, exception.FailureKind);
    }
}
