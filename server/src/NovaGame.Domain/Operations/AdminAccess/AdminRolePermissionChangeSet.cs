namespace NovaGame.Domain.Operations.AdminAccess;

public sealed class AdminRolePermissionChangeSet
{
    internal static AdminRolePermissionChangeSet None { get; } = new([], []);

    internal AdminRolePermissionChangeSet(
        IReadOnlyCollection<RolePermission> added,
        IReadOnlyCollection<RolePermission> removed)
    {
        Added = Array.AsReadOnly(added.ToArray());
        Removed = Array.AsReadOnly(removed.ToArray());
    }

    public bool Changed => Added.Count > 0 || Removed.Count > 0;

    public IReadOnlyList<RolePermission> Added { get; }

    public IReadOnlyList<RolePermission> Removed { get; }
}
