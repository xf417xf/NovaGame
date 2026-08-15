using Microsoft.EntityFrameworkCore;
using NovaGame.Domain.Operations.AdminAccess;

namespace NovaGame.Host.EntityFrameworkCore.Operations.AdminAccess;

public sealed class RefreshTokenSessionRepository : IRefreshTokenSessionRepository
{
    private readonly NovaGameDbContext _dbContext;

    public RefreshTokenSessionRepository(NovaGameDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<RefreshTokenSession?> FindByTokenHashAsync(
        byte[] tokenHash,
        CancellationToken cancellationToken = default)
    {
        var sessions = await _dbContext.RefreshTokenSessions
            .FromSqlInterpolated($"""
                SELECT *
                FROM `admin_refresh_token_sessions`
                WHERE `TokenHash` = {tokenHash}
                LIMIT 1
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return sessions.SingleOrDefault();
    }

    public async Task<IReadOnlyList<RefreshTokenSession>> GetFamilyForUpdateAsync(
        Guid familyId,
        CancellationToken cancellationToken = default)
    {
        var familyIdText = familyId.ToString();
        return await _dbContext.RefreshTokenSessions
            .FromSqlInterpolated($"""
                SELECT *
                FROM `admin_refresh_token_sessions`
                WHERE `FamilyId` = {familyIdText}
                ORDER BY `FamilyId`, `CreatedAtUtc`, `Id`
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(RefreshTokenSession session, CancellationToken cancellationToken = default)
    {
        await _dbContext.RefreshTokenSessions.AddAsync(session, cancellationToken);
    }
}
