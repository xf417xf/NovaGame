using Microsoft.Extensions.Options;
using NovaCore.BuildingBlocks.Data;
using NovaCore.BuildingBlocks.Infrastructure;
using NovaCore.BuildingBlocks.Permissions;
using NovaCore.BuildingBlocks.Timing;
using NovaGame.Domain.Operations.AdminAccess;
using NovaGame.Domain.Shared.Operations.AdminAccess;

namespace NovaGame.Application.Operations.AdminAccess;

public sealed class AdministratorSeedContributor : IDataSeedContributor
{
    private const int MinimumSeedPasswordLength = 12;

    private readonly AdminAccessSeedOptions _options;
    private readonly IAdministratorRepository _administratorRepository;
    private readonly IAdminAccessManagementRepository _managementRepository;
    private readonly AdminAccessManagementPolicy _managementPolicy;
    private readonly IPasswordHashingService _passwordHashingService;
    private readonly IPermissionDefinitionManager _permissionDefinitionManager;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IClock _clock;

    public AdministratorSeedContributor(
        IOptions<AdminAccessSeedOptions> options,
        IAdministratorRepository administratorRepository,
        IAdminAccessManagementRepository managementRepository,
        AdminAccessManagementPolicy managementPolicy,
        IPasswordHashingService passwordHashingService,
        IPermissionDefinitionManager permissionDefinitionManager,
        IGuidGenerator guidGenerator,
        IClock clock)
    {
        _options = options.Value;
        _administratorRepository = administratorRepository;
        _managementRepository = managementRepository;
        _managementPolicy = managementPolicy;
        _passwordHashingService = passwordHashingService;
        _permissionDefinitionManager = permissionDefinitionManager;
        _guidGenerator = guidGenerator;
        _clock = clock;
    }

    public int Order => 100;

    public async Task SeedAsync(DataSeedContext context, CancellationToken cancellationToken = default)
    {
        var hasUserName = !string.IsNullOrWhiteSpace(_options.UserName);
        var hasPassword = !string.IsNullOrWhiteSpace(_options.Password);

        if (!hasUserName || !hasPassword)
        {
            if (hasUserName || hasPassword)
            {
                throw new InvalidOperationException(
                    "Administrator seed configuration requires both a user name and a password.");
            }
        }

        if (hasPassword && _options.Password.Length is < MinimumSeedPasswordLength or > 256)
        {
            throw new InvalidOperationException(
                $"Administrator seed passwords must contain {MinimumSeedPasswordLength} to 256 characters.");
        }

        var registeredPermissionNames = GetRegisteredPermissionNames();
        var now = _clock.UtcNow;
        var builtInRoles = await _managementRepository.GetBuiltInRolesForUpdateAsync(cancellationToken);
        var hasAdministrators = await _administratorRepository.AnyAsync(cancellationToken);
        var role = _managementPolicy.ResolveBuiltInRole(builtInRoles, hasAdministrators);
        if (role is null)
        {
            if (!hasUserName)
            {
                return;
            }

            role = _managementPolicy.CreateBuiltInRole(
                _guidGenerator.Create(),
                registeredPermissionNames,
                now,
                _guidGenerator.Create);
            await _managementRepository.AddRoleAsync(role, cancellationToken);
            await AddSeedAuditAsync(
                AdminAccessAuditAction.RoleCreated,
                now,
                roleId: role.Id,
                targetRevision: role.Revision,
                cancellationToken: cancellationToken);
            await AddPermissionDeltaAuditsAsync(
                role.Permissions,
                [],
                role,
                now,
                cancellationToken);
        }
        else
        {
            var lockedPermissions = await _managementRepository.GetRolePermissionsForUpdateAsync(
                role.Id,
                cancellationToken);
            EnsureLockedPermissionRowsMatch(role, lockedPermissions);
            var changeSet = _managementPolicy.SynchronizeBuiltInRolePermissions(
                role,
                registeredPermissionNames,
                now,
                _guidGenerator.Create);
            await AddPermissionDeltaAuditsAsync(
                changeSet.Added,
                changeSet.Removed,
                role,
                now,
                cancellationToken);
        }

        if (hasAdministrators || !hasUserName)
        {
            return;
        }

        var userName = AdministratorUserName.Create(_options.UserName);
        var administrator = new Administrator(
            _guidGenerator.Create(),
            userName,
            _options.DisplayName,
            _passwordHashingService.Hash(_options.Password),
            now,
            [new AdministratorRoleTarget(_guidGenerator.Create(), role.Id)]);

        await _managementRepository.AddAdministratorAsync(administrator, cancellationToken);
        await AddSeedAuditAsync(
            AdminAccessAuditAction.AdministratorCreated,
            now,
            administrator.Id,
            roleId: role.Id,
            subjectUserName: administrator.UserName,
            targetRevision: administrator.Revision,
            cancellationToken: cancellationToken);
        await AddSeedAuditAsync(
            AdminAccessAuditAction.RoleAssigned,
            now,
            administrator.Id,
            roleId: role.Id,
            subjectUserName: administrator.UserName,
            targetRevision: administrator.Revision,
            cancellationToken: cancellationToken);
    }

    private IReadOnlyList<string> GetRegisteredPermissionNames()
    {
        var names = _permissionDefinitionManager.Groups
            .OrderBy(group => group.Name, StringComparer.Ordinal)
            .SelectMany(group => group.Permissions
                .OrderBy(permission => permission.Name, StringComparer.Ordinal)
                .Select(permission => permission.Name))
            .ToArray();
        return _managementPolicy.CanonicalizeRegisteredPermissionCatalog(names);
    }

    private async Task AddPermissionDeltaAuditsAsync(
        IEnumerable<RolePermission> added,
        IEnumerable<RolePermission> removed,
        AdminRole role,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken)
    {
        foreach (var permission in added.OrderBy(permission => permission.Name, StringComparer.Ordinal))
        {
            await AddSeedAuditAsync(
                AdminAccessAuditAction.PermissionGranted,
                occurredAtUtc,
                roleId: role.Id,
                permissionName: permission.Name,
                targetRevision: role.Revision,
                cancellationToken: cancellationToken);
        }

        foreach (var permission in removed.OrderBy(permission => permission.Name, StringComparer.Ordinal))
        {
            await AddSeedAuditAsync(
                AdminAccessAuditAction.PermissionRevoked,
                occurredAtUtc,
                roleId: role.Id,
                permissionName: permission.Name,
                targetRevision: role.Revision,
                cancellationToken: cancellationToken);
        }
    }

    private static void EnsureLockedPermissionRowsMatch(
        AdminRole role,
        IReadOnlyCollection<RolePermission> permissions)
    {
        if (permissions.Any(permission => permission.RoleId != role.Id) ||
            permissions.Select(permission => permission.Id).Distinct().Count() != permissions.Count)
        {
            throw new AdminAccessManagementDomainException(
                AdminAccessManagementErrorCodes.InvariantViolation,
                "The locked role-permission rows are malformed.");
        }

        var aggregateIds = role.Permissions.Select(permission => permission.Id).Order().ToArray();
        var lockedIds = permissions.Select(permission => permission.Id).Order().ToArray();
        if (!aggregateIds.SequenceEqual(lockedIds))
        {
            throw new AdminAccessManagementDomainException(
                AdminAccessManagementErrorCodes.InvariantViolation,
                "The locked permission rows do not match the role aggregate.");
        }
    }

    private Task AddSeedAuditAsync(
        AdminAccessAuditAction action,
        DateTime occurredAtUtc,
        Guid? administratorId = null,
        Guid? roleId = null,
        string? subjectUserName = null,
        string? permissionName = null,
        long? targetRevision = null,
        CancellationToken cancellationToken = default)
    {
        return _managementRepository.AddAuditAsync(
            CreateSeedAudit(
                action,
                occurredAtUtc,
                administratorId,
                roleId,
                subjectUserName,
                permissionName,
                targetRevision),
            cancellationToken);
    }

    private AdminAccessAuditRecord CreateSeedAudit(
        AdminAccessAuditAction action,
        DateTime occurredAtUtc,
        Guid? administratorId = null,
        Guid? roleId = null,
        string? subjectUserName = null,
        string? permissionName = null,
        long? targetRevision = null)
    {
        return new AdminAccessAuditRecord(
            _guidGenerator.Create(),
            action,
            occurredAtUtc,
            succeeded: true,
            administratorId: administratorId,
            roleId: roleId,
            subjectUserName: subjectUserName,
            permissionName: permissionName,
            targetRevision: targetRevision);
    }
}
