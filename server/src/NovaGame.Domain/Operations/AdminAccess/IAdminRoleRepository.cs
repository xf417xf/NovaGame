namespace NovaGame.Domain.Operations.AdminAccess;

public interface IAdminRoleRepository
{
    Task<AdminRole?> FindByNormalizedNameAsync(
        string normalizedName,
        CancellationToken cancellationToken = default);

    Task AddAsync(AdminRole role, CancellationToken cancellationToken = default);
}
