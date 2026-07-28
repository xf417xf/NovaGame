using Microsoft.EntityFrameworkCore;
using NovaGame.Domain.Operations.AdminAccess;

namespace NovaGame.Host.EntityFrameworkCore.Operations.AdminAccess;

public sealed class AdminRoleRepository : IAdminRoleRepository
{
    private readonly NovaGameDbContext _dbContext;

    public AdminRoleRepository(NovaGameDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<AdminRole?> FindByNormalizedNameAsync(
        string normalizedName,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.AdminRoles
            .Include(role => role.Permissions)
            .SingleOrDefaultAsync(role => role.NormalizedName == normalizedName, cancellationToken);
    }

    public async Task AddAsync(AdminRole role, CancellationToken cancellationToken = default)
    {
        await _dbContext.AdminRoles.AddAsync(role, cancellationToken);
    }
}
