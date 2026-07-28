using NovaCore.BuildingBlocks.Permissions;

namespace NovaGame.Domain.Shared.Operations.AdminAccess;

public sealed class AdminAccessPermissionDefinitionProvider : IPermissionDefinitionProvider
{
    public void Define(PermissionDefinitionContext context)
    {
        var group = context.GetOrAddGroup(AdminAccessPermissions.GroupName, "Administrator access");
        group.AddPermission(
            AdminAccessPermissions.Access,
            "Access operations administration",
            "Allows an administrator to use authenticated operations APIs.");
    }
}
