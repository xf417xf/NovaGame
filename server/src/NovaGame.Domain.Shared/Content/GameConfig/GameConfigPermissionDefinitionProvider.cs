using NovaCore.BuildingBlocks.Permissions;

namespace NovaGame.Domain.Shared.Content.GameConfig;

public sealed class GameConfigPermissionDefinitionProvider : IPermissionDefinitionProvider
{
    public void Define(PermissionDefinitionContext context)
    {
        var group = context.GetOrAddGroup(GameConfigPermissions.GroupName, "Game configuration");
        group.AddPermission(
            GameConfigPermissions.Access,
            "Access game configuration",
            "Allows an administrator to inspect game-configuration drafts, versions, and environments.");
        group.AddPermission(
            GameConfigPermissions.ManageDrafts,
            "Manage game-configuration drafts",
            "Allows an administrator to create, edit, and validate game-configuration drafts.");
        group.AddPermission(
            GameConfigPermissions.Publish,
            "Publish game configuration",
            "Allows an administrator to publish an immutable game-configuration version.");
        group.AddPermission(
            GameConfigPermissions.Activate,
            "Activate game configuration",
            "Allows an administrator to activate or roll back an environment's configuration version.");
    }
}
