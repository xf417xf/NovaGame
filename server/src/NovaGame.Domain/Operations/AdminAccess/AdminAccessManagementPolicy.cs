using NovaGame.Domain.Shared.Operations.AdminAccess;

namespace NovaGame.Domain.Operations.AdminAccess;

public sealed class AdminAccessManagementPolicy
{
    public const string BuiltInRoleName = "Administrators";
    public const string BuiltInRoleNormalizedName = "ADMINISTRATORS";

    public AdminRole CreateCustomRole(
        Guid roleId,
        string name,
        IEnumerable<string> permissionNames,
        IEnumerable<string> registeredPermissionNames,
        Guid createdByAdministratorId,
        DateTime createdAtUtc,
        Func<Guid> grantIdFactory)
    {
        if (createdByAdministratorId == Guid.Empty)
        {
            throw InvalidRequest("The creating administrator identifier cannot be empty.");
        }

        var canonicalPermissionNames = RequireRegisteredPermissionSubset(
            permissionNames,
            registeredPermissionNames);
        var role = new AdminRole(roleId, name, createdAtUtc);
        AddInitialPermissions(
            role,
            canonicalPermissionNames,
            createdAtUtc,
            createdByAdministratorId,
            grantIdFactory);
        return role;
    }

    public AdminRole CreateBuiltInRole(
        Guid roleId,
        IEnumerable<string> registeredPermissionNames,
        DateTime createdAtUtc,
        Func<Guid> grantIdFactory)
    {
        var canonicalPermissionNames = CanonicalizeRegisteredPermissionCatalog(registeredPermissionNames);
        var role = new AdminRole(roleId, BuiltInRoleName, createdAtUtc, isBuiltIn: true);
        AddInitialPermissions(
            role,
            canonicalPermissionNames,
            createdAtUtc,
            grantedByAdministratorId: null,
            grantIdFactory);
        return role;
    }

    public AdminRolePermissionChangeSet ReplaceRolePermissions(
        AdminRole role,
        long expectedRevision,
        IEnumerable<string> permissionNames,
        IEnumerable<string> registeredPermissionNames,
        Guid changedByAdministratorId,
        DateTime changedAtUtc,
        Func<Guid> grantIdFactory)
    {
        ArgumentNullException.ThrowIfNull(role);
        role.EnsureOperatorMutable();
        var canonicalPermissionNames = RequireRegisteredPermissionSubset(
            permissionNames,
            registeredPermissionNames);
        return role.ReplacePermissions(
            expectedRevision,
            canonicalPermissionNames,
            grantIdFactory,
            changedByAdministratorId,
            changedAtUtc);
    }

    public AdminRolePermissionChangeSet SynchronizeBuiltInRolePermissions(
        AdminRole role,
        IEnumerable<string> registeredPermissionNames,
        DateTime changedAtUtc,
        Func<Guid> grantIdFactory)
    {
        ArgumentNullException.ThrowIfNull(role);
        var canonicalPermissionNames = CanonicalizeRegisteredPermissionCatalog(registeredPermissionNames);
        return role.SynchronizeBuiltInPermissions(
            canonicalPermissionNames,
            grantIdFactory,
            changedAtUtc);
    }

    public IReadOnlyList<string> CanonicalizeRegisteredPermissionCatalog(
        IEnumerable<string> registeredPermissionNames)
    {
        return RequireRegisteredPermissionCatalog(registeredPermissionNames);
    }

    public IReadOnlyList<AdminRole> RequireActiveRequestedRoles(
        IEnumerable<Guid> requestedRoleIds,
        IEnumerable<AdminRole> lockedRoles)
    {
        ArgumentNullException.ThrowIfNull(requestedRoleIds);
        ArgumentNullException.ThrowIfNull(lockedRoles);

        var requested = requestedRoleIds.ToArray();
        if (requested.Any(roleId => roleId == Guid.Empty) || requested.Distinct().Count() != requested.Length)
        {
            throw InvalidRequest("Requested role identifiers must be non-empty and unique.");
        }

        var roleLookup = new Dictionary<Guid, AdminRole>();
        foreach (var role in lockedRoles)
        {
            if (role is null || !roleLookup.TryAdd(role.Id, role))
            {
                throw InvariantViolation("Locked role state is malformed or duplicated.");
            }
        }

        var resolved = new List<AdminRole>(requested.Length);
        foreach (var roleId in requested.Order())
        {
            if (!roleLookup.TryGetValue(roleId, out var role))
            {
                throw new AdminAccessManagementDomainException(
                    AdminAccessManagementErrorCodes.RoleNotFound,
                    $"Role '{roleId}' was not found.");
            }

            if (!role.IsActive)
            {
                throw new AdminAccessManagementDomainException(
                    AdminAccessManagementErrorCodes.RoleInactive,
                    $"Role '{roleId}' is inactive.");
            }

            if (role.IsBuiltIn)
            {
                EnsureBuiltInRoleIdentity(role);
            }

            resolved.Add(role);
        }

        return resolved.AsReadOnly();
    }

    public IReadOnlyList<string> RequireRegisteredPermissionSubset(
        IEnumerable<string> permissionNames,
        IEnumerable<string> registeredPermissionNames)
    {
        var registered = RequireRegisteredPermissionCatalog(registeredPermissionNames)
            .ToHashSet(StringComparer.Ordinal);
        var requested = RequirePermissionNames(
            permissionNames,
            AdminAccessManagementErrorCodes.InvalidRequest,
            "Requested permission names must be non-empty, unique, and well formed.");

        foreach (var permissionName in requested)
        {
            if (!registered.Contains(permissionName))
            {
                throw new AdminAccessManagementDomainException(
                    AdminAccessManagementErrorCodes.UnknownPermission,
                    $"Permission '{permissionName}' is not registered.");
            }
        }

        return requested;
    }

    public AdminRole? ResolveBuiltInRole(
        IEnumerable<AdminRole> builtInCandidates,
        bool administratorsExist)
    {
        ArgumentNullException.ThrowIfNull(builtInCandidates);
        var candidates = builtInCandidates.ToArray();
        if (candidates.Length == 0)
        {
            if (administratorsExist)
            {
                throw InvariantViolation("The built-in role is missing for existing administrators.");
            }

            return null;
        }

        if (candidates.Length != 1 || candidates[0] is null)
        {
            throw InvariantViolation("Exactly one built-in role is required.");
        }

        EnsureBuiltInRoleIdentity(candidates[0]);
        return candidates[0];
    }

    public void EnsureBuiltInRoleIdentity(AdminRole role)
    {
        ArgumentNullException.ThrowIfNull(role);
        if (role.Id == Guid.Empty ||
            !role.IsBuiltIn ||
            !role.IsActive ||
            !string.Equals(role.Name, BuiltInRoleName, StringComparison.Ordinal) ||
            !string.Equals(role.NormalizedName, BuiltInRoleNormalizedName, StringComparison.Ordinal))
        {
            throw InvariantViolation("The built-in role identity is malformed.");
        }
    }

    public void EnsureRoleNameAvailable(AdminRole? existingRole, Guid targetRoleId)
    {
        if (targetRoleId == Guid.Empty)
        {
            throw InvalidRequest("The target role identifier cannot be empty.");
        }

        if (existingRole is not null && existingRole.Id != targetRoleId)
        {
            throw new AdminAccessManagementDomainException(
                AdminAccessManagementErrorCodes.RoleNameAlreadyExists,
                "The normalized role name is already in use.");
        }
    }

    public void EnsureCanSetAdministratorActiveState(
        Guid actorAdministratorId,
        Guid targetAdministratorId,
        bool targetIsCurrentlyActive,
        bool requestedIsActive,
        bool targetHasBuiltInRole,
        int activeBuiltInAdministratorCount)
    {
        EnsureAdministratorIdentifiers(actorAdministratorId, targetAdministratorId);
        EnsureActiveBuiltInCount(activeBuiltInAdministratorCount);
        EnsureTargetIsIncludedInActiveBuiltInCount(
            targetIsCurrentlyActive,
            targetHasBuiltInRole,
            activeBuiltInAdministratorCount);

        if (actorAdministratorId == targetAdministratorId && !requestedIsActive)
        {
            throw new AdminAccessManagementDomainException(
                AdminAccessManagementErrorCodes.SelfDeactivationForbidden,
                "An administrator cannot deactivate their own account.");
        }

        if (targetIsCurrentlyActive &&
            !requestedIsActive &&
            targetHasBuiltInRole &&
            activeBuiltInAdministratorCount <= 1)
        {
            throw LastActiveBuiltInAdministratorRequired();
        }
    }

    public void EnsureCanReplaceAdministratorRoles(
        Guid actorAdministratorId,
        Guid targetAdministratorId,
        bool targetIsActive,
        bool targetHasBuiltInRole,
        bool requestedRolesIncludeBuiltInRole,
        int activeBuiltInAdministratorCount)
    {
        EnsureAdministratorIdentifiers(actorAdministratorId, targetAdministratorId);
        EnsureActiveBuiltInCount(activeBuiltInAdministratorCount);
        EnsureTargetIsIncludedInActiveBuiltInCount(
            targetIsActive,
            targetHasBuiltInRole,
            activeBuiltInAdministratorCount);

        if (actorAdministratorId == targetAdministratorId)
        {
            throw new AdminAccessManagementDomainException(
                AdminAccessManagementErrorCodes.SelfRoleMutationForbidden,
                "An administrator cannot replace their own roles.");
        }

        if (targetIsActive &&
            targetHasBuiltInRole &&
            !requestedRolesIncludeBuiltInRole &&
            activeBuiltInAdministratorCount <= 1)
        {
            throw LastActiveBuiltInAdministratorRequired();
        }
    }

    public void EnsureCanResetAdministratorPassword(
        Guid actorAdministratorId,
        Guid targetAdministratorId)
    {
        EnsureAdministratorIdentifiers(actorAdministratorId, targetAdministratorId);
        if (actorAdministratorId == targetAdministratorId)
        {
            throw new AdminAccessManagementDomainException(
                AdminAccessManagementErrorCodes.SelfPasswordResetForbidden,
                "An administrator cannot reset their own password.");
        }
    }

    private static IReadOnlyList<string> RequireRegisteredPermissionCatalog(
        IEnumerable<string> registeredPermissionNames)
    {
        return RequirePermissionNames(
            registeredPermissionNames,
            AdminAccessManagementErrorCodes.InvariantViolation,
            "The registered permission catalog is malformed or duplicated.");
    }

    private static IReadOnlyList<string> RequirePermissionNames(
        IEnumerable<string> permissionNames,
        string failureCode,
        string failureMessage)
    {
        if (permissionNames is null)
        {
            throw new AdminAccessManagementDomainException(failureCode, failureMessage);
        }

        var names = permissionNames.ToArray();
        if (names.Any(name =>
                string.IsNullOrEmpty(name) ||
                name.Length > 128 ||
                name.Any(char.IsControl)) ||
            names.Distinct(StringComparer.Ordinal).Count() != names.Length)
        {
            throw new AdminAccessManagementDomainException(failureCode, failureMessage);
        }

        return Array.AsReadOnly(names.Order(StringComparer.Ordinal).ToArray());
    }

    private static void AddInitialPermissions(
        AdminRole role,
        IReadOnlyList<string> permissionNames,
        DateTime grantedAtUtc,
        Guid? grantedByAdministratorId,
        Func<Guid> grantIdFactory)
    {
        ArgumentNullException.ThrowIfNull(grantIdFactory);
        var grantIds = new HashSet<Guid>();
        foreach (var permissionName in permissionNames)
        {
            var grantId = grantIdFactory();
            if (grantId == Guid.Empty || !grantIds.Add(grantId))
            {
                throw InvalidRequest("Permission grant identifiers must be non-empty and unique.");
            }

            role.GrantPermission(
                grantId,
                permissionName,
                grantedAtUtc,
                grantedByAdministratorId);
        }
    }

    private static void EnsureAdministratorIdentifiers(
        Guid actorAdministratorId,
        Guid targetAdministratorId)
    {
        if (actorAdministratorId == Guid.Empty || targetAdministratorId == Guid.Empty)
        {
            throw InvalidRequest("Actor and target administrator identifiers cannot be empty.");
        }
    }

    private static void EnsureActiveBuiltInCount(int activeBuiltInAdministratorCount)
    {
        if (activeBuiltInAdministratorCount < 0)
        {
            throw InvariantViolation("The active built-in administrator count cannot be negative.");
        }
    }

    private static void EnsureTargetIsIncludedInActiveBuiltInCount(
        bool targetIsActive,
        bool targetHasBuiltInRole,
        int activeBuiltInAdministratorCount)
    {
        if (targetIsActive && targetHasBuiltInRole && activeBuiltInAdministratorCount == 0)
        {
            throw InvariantViolation(
                "An active built-in administrator is missing from the authoritative count.");
        }
    }

    private static AdminAccessManagementDomainException LastActiveBuiltInAdministratorRequired()
    {
        return new AdminAccessManagementDomainException(
            AdminAccessManagementErrorCodes.LastActiveBuiltInAdministratorRequired,
            "At least one active administrator must retain the built-in role.");
    }

    private static AdminAccessManagementDomainException InvalidRequest(string message)
    {
        return new AdminAccessManagementDomainException(
            AdminAccessManagementErrorCodes.InvalidRequest,
            message);
    }

    private static AdminAccessManagementDomainException InvariantViolation(string message)
    {
        return new AdminAccessManagementDomainException(
            AdminAccessManagementErrorCodes.InvariantViolation,
            message);
    }
}
