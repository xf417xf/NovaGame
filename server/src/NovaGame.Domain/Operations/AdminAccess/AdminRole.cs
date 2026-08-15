using NovaCore.BuildingBlocks.Domain;
using NovaGame.Domain.Shared.Operations.AdminAccess;

namespace NovaGame.Domain.Operations.AdminAccess;

public sealed class AdminRole : AggregateRoot<Guid>
{
    private readonly List<RolePermission> _permissions = [];
    private bool _managementIsActive = true;
    private DateTime? _managementUpdatedAtUtc;
    private long _managementRevision = 1;

    private AdminRole()
    {
    }

    public AdminRole(Guid id, string name, DateTime createdAtUtc, bool isBuiltIn = false)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Role ID cannot be empty.", nameof(id));
        }

        var roleName = AdminRoleName.Create(name);
        if (isBuiltIn &&
            (!string.Equals(roleName.Value, AdminAccessManagementPolicy.BuiltInRoleName, StringComparison.Ordinal) ||
             !string.Equals(
                 roleName.NormalizedValue,
                 AdminAccessManagementPolicy.BuiltInRoleNormalizedName,
                 StringComparison.Ordinal)))
        {
            throw new AdminAccessManagementDomainException(
                AdminAccessManagementErrorCodes.InvariantViolation,
                "The built-in role identity is malformed.");
        }

        Id = id;
        Name = roleName.Value;
        NormalizedName = roleName.NormalizedValue;
        IsBuiltIn = isBuiltIn;
        CreatedAtUtc = RequireUtc(createdAtUtc, nameof(createdAtUtc));
    }

    public string Name { get; private set; } = string.Empty;

    public string NormalizedName { get; private set; } = string.Empty;

    public bool IsBuiltIn { get; private set; }

    public bool IsActive => _managementIsActive;

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? UpdatedAtUtc => _managementUpdatedAtUtc;

    public long Revision => _managementRevision;

    public IReadOnlyCollection<RolePermission> Permissions => _permissions.AsReadOnly();

    public bool Rename(long expectedRevision, string name, DateTime changedAtUtc)
    {
        EnsureOperatorMutable();
        var roleName = AdminRoleName.Create(name);
        if (string.Equals(Name, roleName.Value, StringComparison.Ordinal) &&
            string.Equals(NormalizedName, roleName.NormalizedValue, StringComparison.Ordinal))
        {
            return false;
        }

        var transition = PrepareChangedTransition(expectedRevision, changedAtUtc);

        Name = roleName.Value;
        NormalizedName = roleName.NormalizedValue;
        CompleteTransition(transition);
        return true;
    }

    public bool SetActiveState(long expectedRevision, bool isActive, DateTime changedAtUtc)
    {
        EnsureOperatorMutable();
        if (_managementIsActive == isActive)
        {
            return false;
        }

        var transition = PrepareChangedTransition(expectedRevision, changedAtUtc);

        _managementIsActive = isActive;
        CompleteTransition(transition);
        return true;
    }

    internal RolePermission? GrantPermission(
        Guid grantId,
        string permissionName,
        DateTime grantedAtUtc,
        Guid? grantedByAdministratorId = null)
    {
        var normalizedPermissionName = permissionName?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedPermissionName) || normalizedPermissionName.Length > 128)
        {
            throw new ArgumentException("Permission names must contain 1 to 128 characters.", nameof(permissionName));
        }

        if (_permissions.Any(permission => permission.Name == normalizedPermissionName))
        {
            return null;
        }

        var permission = new RolePermission(
            grantId,
            Id,
            normalizedPermissionName,
            RequireUtc(grantedAtUtc, nameof(grantedAtUtc)),
            grantedByAdministratorId);
        _permissions.Add(permission);
        return permission;
    }

    internal AdminRolePermissionChangeSet ReplacePermissions(
        long expectedRevision,
        IReadOnlyList<string> canonicalPermissionNames,
        Func<Guid> grantIdFactory,
        Guid changedByAdministratorId,
        DateTime changedAtUtc)
    {
        EnsureOperatorMutable();
        if (HasExactPermissionSet(canonicalPermissionNames))
        {
            return AdminRolePermissionChangeSet.None;
        }

        var transition = PrepareChangedTransition(expectedRevision, changedAtUtc);
        if (changedByAdministratorId == Guid.Empty)
        {
            throw InvalidRequest("The changing administrator identifier cannot be empty.");
        }

        return ApplyPermissionTarget(
            canonicalPermissionNames,
            grantIdFactory,
            transition,
            changedByAdministratorId);
    }

    internal AdminRolePermissionChangeSet SynchronizeBuiltInPermissions(
        IReadOnlyList<string> canonicalPermissionNames,
        Func<Guid> grantIdFactory,
        DateTime changedAtUtc)
    {
        EnsureBuiltInIdentity();
        if (HasExactPermissionSet(canonicalPermissionNames))
        {
            return AdminRolePermissionChangeSet.None;
        }

        var transition = PrepareChangedTransition(changedAtUtc);
        return ApplyPermissionTarget(
            canonicalPermissionNames,
            grantIdFactory,
            transition,
            changedByAdministratorId: null);
    }

    private AdminRolePermissionChangeSet ApplyPermissionTarget(
        IReadOnlyList<string> canonicalPermissionNames,
        Func<Guid> grantIdFactory,
        PendingTransition transition,
        Guid? changedByAdministratorId)
    {
        ArgumentNullException.ThrowIfNull(grantIdFactory);
        var targetNames = canonicalPermissionNames.ToHashSet(StringComparer.Ordinal);
        var removed = _permissions
            .Where(permission => !targetNames.Contains(permission.Name))
            .ToArray();
        var existingNames = _permissions
            .Select(permission => permission.Name)
            .ToHashSet(StringComparer.Ordinal);
        var added = new List<RolePermission>();

        foreach (var permissionName in canonicalPermissionNames)
        {
            if (existingNames.Contains(permissionName))
            {
                continue;
            }

            var grantId = grantIdFactory();
            if (grantId == Guid.Empty ||
                _permissions.Any(permission => permission.Id == grantId) ||
                added.Any(permission => permission.Id == grantId))
            {
                throw InvalidRequest("Permission grant identifiers must be non-empty and unique.");
            }

            added.Add(new RolePermission(
                grantId,
                Id,
                permissionName,
                transition.ChangedAtUtc,
                changedByAdministratorId));
        }

        _permissions.RemoveAll(permission => !targetNames.Contains(permission.Name));
        _permissions.AddRange(added);
        _permissions.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));
        CompleteTransition(transition);

        return new AdminRolePermissionChangeSet(added, removed);
    }

    private bool HasExactPermissionSet(IReadOnlyList<string> canonicalPermissionNames)
    {
        if (_permissions.Count != canonicalPermissionNames.Count)
        {
            return false;
        }

        var currentNames = _permissions
            .Select(permission => permission.Name)
            .ToHashSet(StringComparer.Ordinal);
        return canonicalPermissionNames.All(currentNames.Contains);
    }

    internal void EnsureOperatorMutable()
    {
        if (IsBuiltIn)
        {
            throw new AdminAccessManagementDomainException(
                AdminAccessManagementErrorCodes.BuiltInRoleImmutable,
                "The built-in role cannot be changed by an operator.");
        }
    }

    private void EnsureBuiltInIdentity()
    {
        if (!IsBuiltIn ||
            !_managementIsActive ||
            !string.Equals(Name, AdminAccessManagementPolicy.BuiltInRoleName, StringComparison.Ordinal) ||
            !string.Equals(
                NormalizedName,
                AdminAccessManagementPolicy.BuiltInRoleNormalizedName,
                StringComparison.Ordinal))
        {
            throw new AdminAccessManagementDomainException(
                AdminAccessManagementErrorCodes.InvariantViolation,
                "The built-in role identity is malformed.");
        }
    }

    private PendingTransition PrepareChangedTransition(
        long expectedRevision,
        DateTime changedAtUtc)
    {
        if (expectedRevision != _managementRevision)
        {
            throw new AdminAccessManagementDomainException(
                AdminAccessManagementErrorCodes.RevisionConflict,
                $"Role revision {expectedRevision} is stale; the current revision is {_managementRevision}.");
        }

        return PrepareChangedTransition(changedAtUtc);
    }

    private PendingTransition PrepareChangedTransition(DateTime changedAtUtc)
    {
        return new PendingTransition(
            RequireUtc(changedAtUtc, nameof(changedAtUtc)),
            checked(_managementRevision + 1));
    }

    private void CompleteTransition(PendingTransition transition)
    {
        _managementUpdatedAtUtc = transition.ChangedAtUtc;
        _managementRevision = transition.Revision;
    }

    private static AdminAccessManagementDomainException InvalidRequest(string message)
    {
        return new AdminAccessManagementDomainException(
            AdminAccessManagementErrorCodes.InvalidRequest,
            message);
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
