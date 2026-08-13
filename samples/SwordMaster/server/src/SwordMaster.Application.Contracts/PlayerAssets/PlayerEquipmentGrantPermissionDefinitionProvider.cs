using NovaCore.BuildingBlocks.Permissions;

namespace SwordMaster.Application.Contracts.PlayerAssets;

public sealed class PlayerEquipmentGrantPermissionDefinitionProvider : IPermissionDefinitionProvider
{
    public void Define(PermissionDefinitionContext context)
    {
        var group = context.GetOrAddGroup(
            PlayerEquipmentGrantPermissions.GroupName,
            "SwordMaster player equipment grants");
        group.AddPermission(
            PlayerEquipmentGrantPermissions.Manage,
            "Manage player equipment grants",
            "Allows an administrator to inspect, grant, and roll back SwordMaster player equipment grants.");
    }
}
