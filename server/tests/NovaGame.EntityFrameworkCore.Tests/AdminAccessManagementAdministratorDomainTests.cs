using NovaGame.Domain.Operations.AdminAccess;
using NovaGame.Domain.Shared.Operations.AdminAccess;
using Xunit;

namespace NovaGame.EntityFrameworkCore.Tests;

public sealed class AdminAccessManagementAdministratorDomainTests
{
    private static readonly Guid AdministratorId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid ActorAdministratorId = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid FirstRoleId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid SecondRoleId = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid ThirdRoleId = Guid.Parse("20000000-0000-0000-0000-000000000003");
    private static readonly Guid FirstMembershipId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid SecondMembershipId = Guid.Parse("30000000-0000-0000-0000-000000000002");
    private static readonly Guid ThirdMembershipId = Guid.Parse("30000000-0000-0000-0000-000000000003");
    private static readonly DateTime CreatedAtUtc = new(2026, 8, 14, 1, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CreationKeepsIdentityImmutableAndCanonicalizesInitialMembershipEvidence()
    {
        var administrator = CreateAdministrator(
            new AdministratorRoleTarget(SecondMembershipId, SecondRoleId),
            new AdministratorRoleTarget(FirstMembershipId, FirstRoleId));

        Assert.Equal("operator", administrator.UserName);
        Assert.Equal("OPERATOR", administrator.NormalizedUserName);
        Assert.Equal("Initial Operator", administrator.DisplayName);
        Assert.Equal(1, administrator.Revision);
        Assert.Null(administrator.UpdatedAtUtc);
        Assert.Equal(
            new[] { FirstRoleId, SecondRoleId },
            administrator.RoleMemberships.Select(membership => membership.RoleId));
        Assert.All(administrator.RoleMemberships, membership =>
        {
            Assert.Equal(AdministratorId, membership.AdministratorId);
            Assert.Equal(ActorAdministratorId, membership.AssignedByAdministratorId);
            Assert.Equal(CreatedAtUtc, membership.AssignedAtUtc);
        });
    }

    [Fact]
    public void DisplayNameIsATrimmedFormKcTargetStateWithStaleNoOpSemantics()
    {
        var administrator = CreateAdministrator();
        var changedAtUtc = CreatedAtUtc.AddMinutes(1);

        var changed = administrator.ChangeDisplayName(
            expectedRevision: 1,
            "  \uFF21lice Operator  ",
            changedAtUtc);

        Assert.True(changed.Changed);
        Assert.Equal(2, changed.Revision);
        Assert.Equal("Alice Operator", administrator.DisplayName);
        Assert.Equal(changedAtUtc, administrator.UpdatedAtUtc);
        Assert.Equal("operator", administrator.UserName);
        Assert.Equal("OPERATOR", administrator.NormalizedUserName);

        var retry = administrator.ChangeDisplayName(
            expectedRevision: 1,
            " Alice Operator ",
            CreatedAtUtc.AddMinutes(2));

        Assert.False(retry.Changed);
        Assert.Equal(2, retry.Revision);
        Assert.Equal(changedAtUtc, administrator.UpdatedAtUtc);

        var conflict = Assert.Throws<AdminAccessManagementDomainException>(() =>
            administrator.ChangeDisplayName(
                expectedRevision: 1,
                "Another Operator",
                CreatedAtUtc.AddMinutes(3)));

        Assert.Equal(AdminAccessManagementErrorCodes.RevisionConflict, conflict.Code);
        Assert.Equal("Alice Operator", administrator.DisplayName);
        Assert.Equal(2, administrator.Revision);
        Assert.Equal(changedAtUtc, administrator.UpdatedAtUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Line\nBreak")]
    public void DisplayNameRejectsEmptyOrControlCharacterValuesWithoutMutation(string displayName)
    {
        var administrator = CreateAdministrator();

        Assert.Throws<ArgumentException>(() => administrator.ChangeDisplayName(
            expectedRevision: 1,
            displayName,
            CreatedAtUtc.AddMinutes(1)));

        Assert.Equal("Initial Operator", administrator.DisplayName);
        Assert.Equal(1, administrator.Revision);
        Assert.Null(administrator.UpdatedAtUtc);
    }

    [Fact]
    public void DisplayNameRejectsValuesLongerThanTheFrozenLimit()
    {
        var administrator = CreateAdministrator();

        Assert.Throws<ArgumentException>(() => administrator.ChangeDisplayName(
            expectedRevision: 1,
            new string('a', 129),
            CreatedAtUtc.AddMinutes(1)));

        Assert.Equal(1, administrator.Revision);
    }

    [Fact]
    public void RoleReplacementPreservesUnchangedEvidenceAndReturnsOrderedDeltas()
    {
        var administrator = CreateAdministrator(
            new AdministratorRoleTarget(SecondMembershipId, SecondRoleId),
            new AdministratorRoleTarget(FirstMembershipId, FirstRoleId));
        var retainedMembership = administrator.RoleMemberships.Single(
            membership => membership.RoleId == FirstRoleId);
        var changedAtUtc = CreatedAtUtc.AddMinutes(1);

        var changed = administrator.ReplaceRoles(
            expectedRevision: 1,
            [
                new AdministratorRoleTarget(ThirdMembershipId, ThirdRoleId),
                new AdministratorRoleTarget(FirstMembershipId, FirstRoleId)
            ],
            ActorAdministratorId,
            changedAtUtc);

        Assert.True(changed.Changed);
        Assert.Equal(2, changed.Revision);
        Assert.Equal(new[] { ThirdRoleId }, changed.AddedRoleIds);
        Assert.Equal(new[] { SecondRoleId }, changed.RemovedRoleIds);
        Assert.Equal(
            new[] { FirstRoleId, ThirdRoleId },
            administrator.RoleMemberships.Select(membership => membership.RoleId));
        Assert.Same(
            retainedMembership,
            administrator.RoleMemberships.Single(membership => membership.RoleId == FirstRoleId));

        var addedMembership = administrator.RoleMemberships.Single(
            membership => membership.RoleId == ThirdRoleId);
        Assert.Equal(ThirdMembershipId, addedMembership.Id);
        Assert.Equal(ActorAdministratorId, addedMembership.AssignedByAdministratorId);
        Assert.Equal(changedAtUtc, addedMembership.AssignedAtUtc);
        Assert.Equal(changedAtUtc, administrator.UpdatedAtUtc);

        var retry = administrator.ReplaceRoles(
            expectedRevision: 1,
            [
                new AdministratorRoleTarget(FirstMembershipId, FirstRoleId),
                new AdministratorRoleTarget(ThirdMembershipId, ThirdRoleId)
            ],
            ActorAdministratorId,
            CreatedAtUtc.AddMinutes(2));

        Assert.False(retry.Changed);
        Assert.Empty(retry.AddedRoleIds);
        Assert.Empty(retry.RemovedRoleIds);
        Assert.Equal(2, retry.Revision);
        Assert.Equal(changedAtUtc, administrator.UpdatedAtUtc);
        Assert.Same(
            retainedMembership,
            administrator.RoleMemberships.Single(membership => membership.RoleId == FirstRoleId));
        Assert.Same(
            addedMembership,
            administrator.RoleMemberships.Single(membership => membership.RoleId == ThirdRoleId));
    }

    [Fact]
    public void RoleReplacementAcceptsEmptyStateAndAdvancesOneRevisionForEveryDelta()
    {
        var administrator = CreateAdministrator(
            new AdministratorRoleTarget(FirstMembershipId, FirstRoleId),
            new AdministratorRoleTarget(SecondMembershipId, SecondRoleId));

        var transition = administrator.ReplaceRoles(
            expectedRevision: 1,
            [],
            ActorAdministratorId,
            CreatedAtUtc.AddMinutes(1));

        Assert.True(transition.Changed);
        Assert.Empty(transition.AddedRoleIds);
        Assert.Equal(new[] { FirstRoleId, SecondRoleId }, transition.RemovedRoleIds);
        Assert.Empty(administrator.RoleMemberships);
        Assert.Equal(2, administrator.Revision);
    }

    [Fact]
    public void RoleReplacementRejectsStaleChangesAndMalformedTargetsAtomically()
    {
        var administrator = CreateAdministrator(
            new AdministratorRoleTarget(FirstMembershipId, FirstRoleId));

        var conflict = Assert.Throws<AdminAccessManagementDomainException>(() =>
            administrator.ReplaceRoles(
                expectedRevision: 0,
                [new AdministratorRoleTarget(SecondMembershipId, SecondRoleId)],
                ActorAdministratorId,
                CreatedAtUtc.AddMinutes(1)));
        Assert.Equal(AdminAccessManagementErrorCodes.RevisionConflict, conflict.Code);

        var duplicateRole = Assert.Throws<AdminAccessManagementDomainException>(() =>
            administrator.ReplaceRoles(
                expectedRevision: 1,
                [
                    new AdministratorRoleTarget(FirstMembershipId, FirstRoleId),
                    new AdministratorRoleTarget(SecondMembershipId, FirstRoleId)
                ],
                ActorAdministratorId,
                CreatedAtUtc.AddMinutes(1)));
        Assert.Equal(AdminAccessManagementErrorCodes.InvalidRequest, duplicateRole.Code);

        var changedEvidence = Assert.Throws<AdminAccessManagementDomainException>(() =>
            administrator.ReplaceRoles(
                expectedRevision: 1,
                [new AdministratorRoleTarget(SecondMembershipId, FirstRoleId)],
                ActorAdministratorId,
                CreatedAtUtc.AddMinutes(1)));
        Assert.Equal(AdminAccessManagementErrorCodes.InvalidRequest, changedEvidence.Code);

        Assert.Equal(1, administrator.Revision);
        Assert.Null(administrator.UpdatedAtUtc);
        var membership = Assert.Single(administrator.RoleMemberships);
        Assert.Equal(FirstMembershipId, membership.Id);
        Assert.Equal(FirstRoleId, membership.RoleId);
    }

    [Fact]
    public void RoleReplacementRejectsEmptyAndDuplicateMembershipIdentifiersAtomically()
    {
        var administrator = CreateAdministrator(
            new AdministratorRoleTarget(FirstMembershipId, FirstRoleId));

        var emptyRoleId = Assert.Throws<AdminAccessManagementDomainException>(() =>
            administrator.ReplaceRoles(
                expectedRevision: 1,
                [new AdministratorRoleTarget(SecondMembershipId, Guid.Empty)],
                ActorAdministratorId,
                CreatedAtUtc.AddMinutes(1)));
        var emptyMembershipId = Assert.Throws<AdminAccessManagementDomainException>(() =>
            administrator.ReplaceRoles(
                expectedRevision: 1,
                [new AdministratorRoleTarget(Guid.Empty, SecondRoleId)],
                ActorAdministratorId,
                CreatedAtUtc.AddMinutes(1)));
        var duplicateMembershipId = Assert.Throws<AdminAccessManagementDomainException>(() =>
            administrator.ReplaceRoles(
                expectedRevision: 1,
                [
                    new AdministratorRoleTarget(SecondMembershipId, SecondRoleId),
                    new AdministratorRoleTarget(SecondMembershipId, ThirdRoleId)
                ],
                ActorAdministratorId,
                CreatedAtUtc.AddMinutes(1)));

        Assert.Equal(AdminAccessManagementErrorCodes.InvalidRequest, emptyRoleId.Code);
        Assert.Equal(AdminAccessManagementErrorCodes.InvalidRequest, emptyMembershipId.Code);
        Assert.Equal(AdminAccessManagementErrorCodes.InvalidRequest, duplicateMembershipId.Code);
        Assert.Equal(1, administrator.Revision);
        Assert.Null(administrator.UpdatedAtUtc);
        var membership = Assert.Single(administrator.RoleMemberships);
        Assert.Equal(FirstMembershipId, membership.Id);
        Assert.Equal(FirstRoleId, membership.RoleId);
    }

    [Fact]
    public void ActiveStateSettersAdvanceOnceAndCanonicalRetriesIgnoreStaleRevisions()
    {
        var administrator = CreateAdministrator();
        var deactivatedAtUtc = CreatedAtUtc.AddMinutes(1);

        var deactivated = administrator.SetActiveState(
            expectedRevision: 1,
            isActive: false,
            deactivatedAtUtc);

        Assert.True(deactivated.Changed);
        Assert.False(administrator.IsActive);
        Assert.Equal(2, administrator.Revision);
        Assert.Equal(deactivatedAtUtc, administrator.UpdatedAtUtc);

        var retry = administrator.SetActiveState(
            expectedRevision: 1,
            isActive: false,
            CreatedAtUtc.AddMinutes(2));
        Assert.False(retry.Changed);
        Assert.Equal(2, retry.Revision);
        Assert.Equal(deactivatedAtUtc, administrator.UpdatedAtUtc);

        var conflict = Assert.Throws<AdminAccessManagementDomainException>(() =>
            administrator.SetActiveState(
                expectedRevision: 1,
                isActive: true,
                CreatedAtUtc.AddMinutes(3)));
        Assert.Equal(AdminAccessManagementErrorCodes.RevisionConflict, conflict.Code);
        Assert.False(administrator.IsActive);

        var activated = administrator.SetActiveState(
            expectedRevision: 2,
            isActive: true,
            CreatedAtUtc.AddMinutes(4));
        Assert.True(activated.Changed);
        Assert.True(administrator.IsActive);
        Assert.Equal(3, administrator.Revision);
    }

    [Fact]
    public void LoginDoesNotAdvanceManagementRevisionWhileRehashAndResetAlwaysDo()
    {
        var administrator = CreateAdministrator();

        administrator.RecordSuccessfulLogin(CreatedAtUtc.AddMinutes(1));

        Assert.Equal(1, administrator.Revision);
        Assert.Equal(CreatedAtUtc.AddMinutes(1), administrator.LastLoginAtUtc);
        Assert.Equal(CreatedAtUtc.AddMinutes(1), administrator.UpdatedAtUtc);

        var rehashed = administrator.RehashPasswordHash(
            "hash-v1",
            CreatedAtUtc.AddMinutes(2));
        Assert.True(rehashed.Changed);
        Assert.Equal(2, administrator.Revision);
        Assert.Equal(CreatedAtUtc.AddMinutes(2), administrator.UpdatedAtUtc);

        administrator.RecordSuccessfulLogin(CreatedAtUtc.AddMinutes(3));
        Assert.Equal(2, administrator.Revision);
        Assert.Equal(CreatedAtUtc.AddMinutes(3), administrator.UpdatedAtUtc);

        var reset = administrator.ResetPasswordHash(
            expectedRevision: 2,
            "hash-v2",
            CreatedAtUtc.AddMinutes(4));
        Assert.True(reset.Changed);
        Assert.Equal(3, administrator.Revision);
        Assert.Equal("hash-v2", administrator.PasswordHash);

        var conflict = Assert.Throws<AdminAccessManagementDomainException>(() =>
            administrator.ResetPasswordHash(
                expectedRevision: 2,
                "hash-v3",
                CreatedAtUtc.AddMinutes(5)));
        Assert.Equal(AdminAccessManagementErrorCodes.RevisionConflict, conflict.Code);
        Assert.Equal("hash-v2", administrator.PasswordHash);
        Assert.Equal(3, administrator.Revision);

        var sameHashReset = administrator.ResetPasswordHash(
            expectedRevision: 3,
            "hash-v2",
            CreatedAtUtc.AddMinutes(6));
        Assert.True(sameHashReset.Changed);
        Assert.Equal(4, administrator.Revision);
    }

    [Fact]
    public void ChangedTransitionsRequireUtcWithoutPartialMutation()
    {
        var administrator = CreateAdministrator();
        var localTime = DateTime.SpecifyKind(CreatedAtUtc.AddMinutes(1), DateTimeKind.Local);

        Assert.Throws<ArgumentException>(() => administrator.ChangeDisplayName(
            expectedRevision: 1,
            "Changed Operator",
            localTime));

        Assert.Equal("Initial Operator", administrator.DisplayName);
        Assert.Equal(1, administrator.Revision);
        Assert.Null(administrator.UpdatedAtUtc);
    }

    [Fact]
    public void RoleAndMembershipTransitionsRejectNonUtcTimeWithoutPartialMutation()
    {
        var administrator = CreateAdministrator(
            new AdministratorRoleTarget(FirstMembershipId, FirstRoleId));
        var role = new AdminRole(SecondRoleId, "Operators", CreatedAtUtc);
        var localTime = DateTime.SpecifyKind(CreatedAtUtc.AddMinutes(1), DateTimeKind.Local);

        Assert.Throws<ArgumentException>(() => role.Rename(1, "Reviewers", localTime));
        Assert.Throws<ArgumentException>(() => administrator.ReplaceRoles(
            expectedRevision: 1,
            [
                new AdministratorRoleTarget(FirstMembershipId, FirstRoleId),
                new AdministratorRoleTarget(SecondMembershipId, SecondRoleId)
            ],
            ActorAdministratorId,
            localTime));

        Assert.Equal("Operators", role.Name);
        Assert.Equal(1, role.Revision);
        Assert.Null(role.UpdatedAtUtc);
        Assert.Equal(1, administrator.Revision);
        Assert.Null(administrator.UpdatedAtUtc);
        var membership = Assert.Single(administrator.RoleMemberships);
        Assert.Equal(FirstMembershipId, membership.Id);
        Assert.Equal(FirstRoleId, membership.RoleId);
    }

    private static Administrator CreateAdministrator(params AdministratorRoleTarget[] initialRoles)
    {
        return new Administrator(
            AdministratorId,
            AdministratorUserName.Create("operator"),
            " Initial Operator ",
            "hash-v1",
            CreatedAtUtc,
            initialRoles,
            ActorAdministratorId);
    }
}
