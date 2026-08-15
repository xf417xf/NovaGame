using NovaCore.BuildingBlocks.Domain;
using NovaGame.Domain.Shared.Operations.AdminAccess;

namespace NovaGame.Domain.Operations.AdminAccess;

public sealed class Administrator : AggregateRoot<Guid>
{
    private readonly List<AdministratorRole> _roleMemberships = [];
    private long _revision = 1;

    private Administrator()
    {
    }

    public Administrator(
        Guid id,
        AdministratorUserName userName,
        string displayName,
        string passwordHash,
        DateTime createdAtUtc,
        bool isActive = true)
        : this(
            id,
            userName,
            displayName,
            passwordHash,
            createdAtUtc,
            [],
            createdByAdministratorId: null,
            isActive: isActive)
    {
    }

    public Administrator(
        Guid id,
        AdministratorUserName userName,
        string displayName,
        string passwordHash,
        DateTime createdAtUtc,
        IReadOnlyCollection<AdministratorRoleTarget> initialRoles,
        Guid? createdByAdministratorId = null,
        bool isActive = true)
    {
        ArgumentNullException.ThrowIfNull(userName);

        if (id == Guid.Empty)
        {
            throw new ArgumentException("Administrator ID cannot be empty.", nameof(id));
        }

        Id = id;
        UserName = userName.Value;
        NormalizedUserName = userName.NormalizedValue;
        DisplayName = AdministratorDisplayName.Create(displayName).Value;
        PasswordHash = RequirePasswordHash(passwordHash);
        IsActive = isActive;
        CreatedAtUtc = RequireUtc(createdAtUtc, nameof(createdAtUtc));

        var canonicalRoles = CanonicalizeRoleTargets(initialRoles);
        _roleMemberships.AddRange(canonicalRoles.Select(target => new AdministratorRole(
            target.MembershipId,
            Id,
            target.RoleId,
            CreatedAtUtc,
            createdByAdministratorId)));
    }

    public string UserName { get; private set; } = string.Empty;

    public string NormalizedUserName { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public string PasswordHash { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? UpdatedAtUtc { get; private set; }

    public DateTime? LastLoginAtUtc { get; private set; }

    public long Revision => _revision;

    public IReadOnlyCollection<AdministratorRole> RoleMemberships => _roleMemberships.AsReadOnly();

    public void SetPasswordHash(string passwordHash, DateTime changedAtUtc)
    {
        _ = RehashPasswordHash(passwordHash, changedAtUtc);
    }

    public AdministratorTransition RehashPasswordHash(string passwordHash, DateTime changedAtUtc)
    {
        var requiredPasswordHash = RequirePasswordHash(passwordHash);
        var transition = PrepareChangedTransition(changedAtUtc);

        PasswordHash = requiredPasswordHash;
        CompleteTransition(transition);
        return new AdministratorTransition(true, _revision);
    }

    public AdministratorTransition ResetPasswordHash(
        long expectedRevision,
        string passwordHash,
        DateTime changedAtUtc)
    {
        var requiredPasswordHash = RequirePasswordHash(passwordHash);
        var transition = PrepareChangedTransition(expectedRevision, changedAtUtc);

        PasswordHash = requiredPasswordHash;
        CompleteTransition(transition);
        return new AdministratorTransition(true, _revision);
    }

    public void RecordSuccessfulLogin(DateTime loggedInAtUtc)
    {
        LastLoginAtUtc = RequireUtc(loggedInAtUtc, nameof(loggedInAtUtc));
        UpdatedAtUtc = loggedInAtUtc;
    }

    public AdministratorTransition ChangeDisplayName(
        long expectedRevision,
        string displayName,
        DateTime changedAtUtc)
    {
        var normalizedDisplayName = AdministratorDisplayName.Create(displayName).Value;
        if (string.Equals(DisplayName, normalizedDisplayName, StringComparison.Ordinal))
        {
            return new AdministratorTransition(false, _revision);
        }

        var transition = PrepareChangedTransition(expectedRevision, changedAtUtc);
        DisplayName = normalizedDisplayName;
        CompleteTransition(transition);
        return new AdministratorTransition(true, _revision);
    }

    public AdministratorRole AssignRole(
        Guid membershipId,
        Guid roleId,
        DateTime assignedAtUtc,
        Guid? assignedByAdministratorId = null)
    {
        if (_roleMemberships.Any(membership => membership.RoleId == roleId))
        {
            throw new InvalidOperationException("The administrator already belongs to this role.");
        }

        var membership = new AdministratorRole(
            membershipId,
            Id,
            roleId,
            RequireUtc(assignedAtUtc, nameof(assignedAtUtc)),
            assignedByAdministratorId);
        _roleMemberships.Add(membership);
        _roleMemberships.Sort(static (left, right) => left.RoleId.CompareTo(right.RoleId));
        UpdatedAtUtc = assignedAtUtc;
        return membership;
    }

    public AdministratorRoleReplacementTransition ReplaceRoles(
        long expectedRevision,
        IReadOnlyCollection<AdministratorRoleTarget> targetRoles,
        Guid assignedByAdministratorId,
        DateTime changedAtUtc)
    {
        var canonicalTargets = CanonicalizeRoleTargets(targetRoles);
        var currentMemberships = _roleMemberships.ToDictionary(membership => membership.RoleId);

        if (assignedByAdministratorId == Guid.Empty)
        {
            throw InvalidRoleTargets("The assigning administrator identifier cannot be empty.");
        }

        foreach (var target in canonicalTargets)
        {
            if (currentMemberships.TryGetValue(target.RoleId, out var currentMembership) &&
                currentMembership.Id != target.MembershipId)
            {
                throw InvalidRoleTargets(
                    "Existing role memberships must retain their original membership identifiers.");
            }
        }

        var currentRoleIds = currentMemberships.Keys.Order().ToArray();
        var targetRoleIds = canonicalTargets.Select(target => target.RoleId).ToArray();
        if (currentRoleIds.SequenceEqual(targetRoleIds))
        {
            return new AdministratorRoleReplacementTransition(
                false,
                _revision,
                [],
                []);
        }

        var transition = PrepareChangedTransition(expectedRevision, changedAtUtc);
        var addedTargets = canonicalTargets
            .Where(target => !currentMemberships.ContainsKey(target.RoleId))
            .ToArray();
        var removedMemberships = _roleMemberships
            .Where(membership => !targetRoleIds.Contains(membership.RoleId))
            .OrderBy(membership => membership.RoleId)
            .ToArray();
        var retainedMembershipIds = currentMemberships.Values
            .Select(membership => membership.Id)
            .ToHashSet();

        if (addedTargets.Any(target => retainedMembershipIds.Contains(target.MembershipId)))
        {
            throw InvalidRoleTargets(
                "New role memberships cannot reuse an existing membership identifier.");
        }

        var addedMemberships = addedTargets
            .Select(target => new AdministratorRole(
                target.MembershipId,
                Id,
                target.RoleId,
                transition.ChangedAtUtc,
                assignedByAdministratorId))
            .ToArray();

        foreach (var membership in removedMemberships)
        {
            _roleMemberships.Remove(membership);
        }

        _roleMemberships.AddRange(addedMemberships);
        _roleMemberships.Sort(static (left, right) => left.RoleId.CompareTo(right.RoleId));
        CompleteTransition(transition);

        return new AdministratorRoleReplacementTransition(
            true,
            _revision,
            addedMemberships.Select(membership => membership.RoleId).ToArray(),
            removedMemberships.Select(membership => membership.RoleId).ToArray());
    }

    public AdministratorTransition SetActiveState(
        long expectedRevision,
        bool isActive,
        DateTime changedAtUtc)
    {
        if (IsActive == isActive)
        {
            return new AdministratorTransition(false, _revision);
        }

        var transition = PrepareChangedTransition(expectedRevision, changedAtUtc);
        IsActive = isActive;
        CompleteTransition(transition);
        return new AdministratorTransition(true, _revision);
    }

    public void Activate(DateTime changedAtUtc)
    {
        _ = SetActiveState(_revision, isActive: true, changedAtUtc);
    }

    public void Deactivate(DateTime changedAtUtc)
    {
        _ = SetActiveState(_revision, isActive: false, changedAtUtc);
    }

    private static string RequirePasswordHash(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 512)
        {
            throw new ArgumentException("A valid password hash is required.", nameof(value));
        }

        return value;
    }

    private static AdministratorRoleTarget[] CanonicalizeRoleTargets(
        IReadOnlyCollection<AdministratorRoleTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);

        if (targets.Any(target => target.RoleId == Guid.Empty || target.MembershipId == Guid.Empty) ||
            targets.Select(target => target.RoleId).Distinct().Count() != targets.Count ||
            targets.Select(target => target.MembershipId).Distinct().Count() != targets.Count)
        {
            throw InvalidRoleTargets(
                "Role targets require unique, non-empty role and membership identifiers.");
        }

        return targets.OrderBy(target => target.RoleId).ToArray();
    }

    private static AdminAccessManagementDomainException InvalidRoleTargets(string message)
    {
        return new AdminAccessManagementDomainException(
            AdminAccessManagementErrorCodes.InvalidRequest,
            message);
    }

    private PendingTransition PrepareChangedTransition(
        long expectedRevision,
        DateTime changedAtUtc)
    {
        if (expectedRevision != _revision)
        {
            throw new AdminAccessManagementDomainException(
                AdminAccessManagementErrorCodes.RevisionConflict,
                $"Administrator revision {expectedRevision} is stale; the current revision is {_revision}.");
        }

        return PrepareChangedTransition(changedAtUtc);
    }

    private PendingTransition PrepareChangedTransition(DateTime changedAtUtc)
    {
        return new PendingTransition(
            RequireUtc(changedAtUtc, nameof(changedAtUtc)),
            checked(_revision + 1));
    }

    private void CompleteTransition(PendingTransition transition)
    {
        UpdatedAtUtc = transition.ChangedAtUtc;
        _revision = transition.Revision;
    }

    private static DateTime RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The timestamp must use UTC.", parameterName);
        }

        return value;
    }

    private readonly record struct PendingTransition(DateTime ChangedAtUtc, long Revision);
}
