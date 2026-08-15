using Microsoft.EntityFrameworkCore;
using NovaGame.Domain.Operations.AdminAccess;

namespace NovaGame.Host.EntityFrameworkCore.Operations.AdminAccess;

public sealed class AdministratorRepository : IAdministratorRepository
{
    private readonly NovaGameDbContext _dbContext;

    public AdministratorRepository(NovaGameDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<bool> AnyAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.Administrators.AnyAsync(cancellationToken);
    }

    public Task<Administrator?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _dbContext.Administrators.SingleOrDefaultAsync(
            administrator => administrator.Id == id,
            cancellationToken);
    }

    public async Task<Administrator?> FindByIdForUpdateAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var idText = id.ToString();
        var administrators = await _dbContext.Administrators
            .FromSqlInterpolated($"""
                SELECT *
                FROM `admin_administrators`
                WHERE `Id` = {idText}
                ORDER BY `Id`
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);
        return administrators.SingleOrDefault();
    }

    public Task<Administrator?> FindByNormalizedUserNameAsync(
        string normalizedUserName,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Administrators.SingleOrDefaultAsync(
            administrator => administrator.NormalizedUserName == normalizedUserName,
            cancellationToken);
    }

    public async Task<Administrator?> FindByNormalizedUserNameForUpdateAsync(
        string normalizedUserName,
        CancellationToken cancellationToken = default)
    {
        var administrators = await _dbContext.Administrators
            .FromSqlInterpolated($"""
                SELECT *
                FROM `admin_administrators`
                WHERE `NormalizedUserName` = {normalizedUserName}
                ORDER BY `NormalizedUserName`, `Id`
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);
        return administrators.SingleOrDefault();
    }

    public async Task<AdministratorAuthorizationSnapshot?> GetAuthorizationSnapshotAsync(
        Guid administratorId,
        CancellationToken cancellationToken = default)
    {
        var administrator = await _dbContext.Administrators
            .AsNoTracking()
            .Where(candidate => candidate.Id == administratorId)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.UserName,
                candidate.DisplayName,
                candidate.IsActive
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (administrator is null)
        {
            return null;
        }

        var roleIds = await _dbContext.AdministratorRoles
            .AsNoTracking()
            .Where(membership => membership.AdministratorId == administratorId)
            .Select(membership => membership.RoleId)
            .ToArrayAsync(cancellationToken);

        var roleNames = roleIds.Length == 0
            ? []
            : await _dbContext.AdminRoles
                .AsNoTracking()
                .Where(role => roleIds.Contains(role.Id))
                .Select(role => role.Name)
                .Distinct()
                .OrderBy(name => name)
                .ToArrayAsync(cancellationToken);

        var activeRoleIds = roleIds.Length == 0
            ? []
            : await _dbContext.AdminRoles
                .AsNoTracking()
                .Where(role => roleIds.Contains(role.Id) && role.IsActive)
                .Select(role => role.Id)
                .ToArrayAsync(cancellationToken);

        var permissionNames = activeRoleIds.Length == 0
            ? []
            : await _dbContext.RolePermissions
                .AsNoTracking()
                .Where(permission => activeRoleIds.Contains(permission.RoleId))
                .Select(permission => permission.Name)
                .Distinct()
                .OrderBy(name => name)
                .ToArrayAsync(cancellationToken);

        return new AdministratorAuthorizationSnapshot(
            administrator.Id,
            administrator.UserName,
            administrator.DisplayName,
            administrator.IsActive,
            roleNames,
            permissionNames);
    }

    public async Task AddAsync(Administrator administrator, CancellationToken cancellationToken = default)
    {
        await _dbContext.Administrators.AddAsync(administrator, cancellationToken);
    }
}
