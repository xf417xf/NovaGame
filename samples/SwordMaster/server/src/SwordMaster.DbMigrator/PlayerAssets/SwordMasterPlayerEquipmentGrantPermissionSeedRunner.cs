using NovaCore.BuildingBlocks.Infrastructure;
using NovaCore.BuildingBlocks.Permissions;
using NovaCore.BuildingBlocks.Timing;
using NovaGame.Domain.Operations.AdminAccess;
using SwordMaster.Application.Contracts.PlayerAssets;

namespace SwordMaster.DbMigrator.PlayerAssets;

public sealed class SwordMasterPlayerEquipmentGrantPermissionSeedRunner
{
    private const string AdministratorRoleName = "Administrators";
    private const string NormalizedAdministratorRoleName = "ADMINISTRATORS";

    private readonly IAdminRoleRepository _roleRepository;
    private readonly IAdminAccessAuditRepository _auditRepository;
    private readonly IPermissionDefinitionManager _permissionDefinitionManager;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IClock _clock;

    public SwordMasterPlayerEquipmentGrantPermissionSeedRunner(
        IAdminRoleRepository roleRepository,
        IAdminAccessAuditRepository auditRepository,
        IPermissionDefinitionManager permissionDefinitionManager,
        IGuidGenerator guidGenerator,
        IClock clock)
    {
        _roleRepository = roleRepository;
        _auditRepository = auditRepository;
        _permissionDefinitionManager = permissionDefinitionManager;
        _guidGenerator = guidGenerator;
        _clock = clock;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (_permissionDefinitionManager.GetOrNull(
                PlayerEquipmentGrantPermissions.Manage) is null)
        {
            throw new InvalidOperationException(
                $"Required permission '{PlayerEquipmentGrantPermissions.Manage}' is not defined.");
        }

        var role = await _roleRepository.FindByNormalizedNameAsync(
            NormalizedAdministratorRoleName,
            cancellationToken);
        if (role is null)
        {
            return;
        }

        if (!role.IsBuiltIn ||
            !string.Equals(role.Name, AdministratorRoleName, StringComparison.Ordinal) ||
            !string.Equals(
                role.NormalizedName,
                NormalizedAdministratorRoleName,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The built-in '{AdministratorRoleName}' role is required before seeding SwordMaster permissions.");
        }

        var now = _clock.UtcNow;
        var permission = role.GrantPermission(
            _guidGenerator.Create(),
            PlayerEquipmentGrantPermissions.Manage,
            now);
        if (permission is null)
        {
            return;
        }

        await _auditRepository.AddAsync(
            new AdminAccessAuditRecord(
                _guidGenerator.Create(),
                AdminAccessAuditAction.PermissionGranted,
                now,
                succeeded: true,
                roleId: role.Id,
                permissionName: permission.Name),
            cancellationToken);
    }
}
