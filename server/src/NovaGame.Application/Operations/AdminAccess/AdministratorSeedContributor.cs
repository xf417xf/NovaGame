using Microsoft.Extensions.Options;
using NovaCore.BuildingBlocks.Data;
using NovaCore.BuildingBlocks.Infrastructure;
using NovaCore.BuildingBlocks.Permissions;
using NovaCore.BuildingBlocks.Timing;
using NovaGame.Domain.Operations.AdminAccess;
using NovaGame.Domain.Shared.Content.GameConfig;
using NovaGame.Domain.Shared.Operations.AdminAccess;

namespace NovaGame.Application.Operations.AdminAccess;

public sealed class AdministratorSeedContributor : IDataSeedContributor
{
    private const string AdministratorRoleName = "Administrators";
    private const int MinimumSeedPasswordLength = 12;

    private static readonly string[] AdministratorPermissionNames =
    [
        AdminAccessPermissions.Access,
        GameConfigPermissions.Access,
        GameConfigPermissions.ManageDrafts,
        GameConfigPermissions.Publish,
        GameConfigPermissions.Activate
    ];

    private readonly AdminAccessSeedOptions _options;
    private readonly IAdministratorRepository _administratorRepository;
    private readonly IAdminRoleRepository _roleRepository;
    private readonly IAdminAccessAuditRepository _auditRepository;
    private readonly IPasswordHashingService _passwordHashingService;
    private readonly IPermissionDefinitionManager _permissionDefinitionManager;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IClock _clock;

    public AdministratorSeedContributor(
        IOptions<AdminAccessSeedOptions> options,
        IAdministratorRepository administratorRepository,
        IAdminRoleRepository roleRepository,
        IAdminAccessAuditRepository auditRepository,
        IPasswordHashingService passwordHashingService,
        IPermissionDefinitionManager permissionDefinitionManager,
        IGuidGenerator guidGenerator,
        IClock clock)
    {
        _options = options.Value;
        _administratorRepository = administratorRepository;
        _roleRepository = roleRepository;
        _auditRepository = auditRepository;
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

        foreach (var permissionName in AdministratorPermissionNames)
        {
            if (_permissionDefinitionManager.GetOrNull(permissionName) is null)
            {
                throw new InvalidOperationException(
                    $"Required permission '{permissionName}' is not defined.");
            }
        }

        var hasAdministrators = await _administratorRepository.AnyAsync(cancellationToken);
        var now = _clock.UtcNow;
        var normalizedRoleName = AdministratorRoleName.ToUpperInvariant();
        var role = await _roleRepository.FindByNormalizedNameAsync(normalizedRoleName, cancellationToken);
        if (role is null)
        {
            if (hasAdministrators)
            {
                throw new InvalidOperationException(
                    $"The built-in '{AdministratorRoleName}' role is missing for existing administrators.");
            }

            if (!hasUserName)
            {
                return;
            }

            role = new AdminRole(_guidGenerator.Create(), AdministratorRoleName, now, isBuiltIn: true);
            await _roleRepository.AddAsync(role, cancellationToken);
            await _auditRepository.AddAsync(
                CreateSeedAudit(AdminAccessAuditAction.RoleCreated, now, roleId: role.Id),
                cancellationToken);
        }

        foreach (var permissionName in AdministratorPermissionNames)
        {
            var permission = role.GrantPermission(
                _guidGenerator.Create(),
                permissionName,
                now);
            if (permission is not null)
            {
                await _auditRepository.AddAsync(
                    CreateSeedAudit(
                        AdminAccessAuditAction.PermissionGranted,
                        now,
                        roleId: role.Id,
                        permissionName: permission.Name),
                    cancellationToken);
            }
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
            now);
        administrator.AssignRole(_guidGenerator.Create(), role.Id, now);

        await _administratorRepository.AddAsync(administrator, cancellationToken);
        await _auditRepository.AddAsync(
            CreateSeedAudit(
                AdminAccessAuditAction.AdministratorCreated,
                now,
                administrator.Id,
                roleId: role.Id,
                subjectUserName: administrator.UserName),
            cancellationToken);
        await _auditRepository.AddAsync(
            CreateSeedAudit(
                AdminAccessAuditAction.RoleAssigned,
                now,
                administrator.Id,
                roleId: role.Id,
                subjectUserName: administrator.UserName),
            cancellationToken);
    }

    private AdminAccessAuditRecord CreateSeedAudit(
        AdminAccessAuditAction action,
        DateTime occurredAtUtc,
        Guid? administratorId = null,
        Guid? roleId = null,
        string? subjectUserName = null,
        string? permissionName = null)
    {
        return new AdminAccessAuditRecord(
            _guidGenerator.Create(),
            action,
            occurredAtUtc,
            succeeded: true,
            administratorId: administratorId,
            roleId: roleId,
            subjectUserName: subjectUserName,
            permissionName: permissionName);
    }
}
