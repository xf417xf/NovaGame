using Microsoft.EntityFrameworkCore;
using NovaGame.Domain.Content.GameConfig;

namespace NovaGame.Host.EntityFrameworkCore.Content.GameConfig;

public sealed class GameConfigEnvironmentRepository : IGameConfigEnvironmentRepository
{
    private readonly NovaGameDbContext _dbContext;

    public GameConfigEnvironmentRepository(NovaGameDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<GameConfigEnvironment?> FindAsync(
        string configurationSetKey,
        string environmentName,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.GameConfigEnvironments.SingleOrDefaultAsync(
            environment =>
                environment.ConfigurationSetKey == configurationSetKey &&
                environment.EnvironmentName == environmentName,
            cancellationToken);
    }

    public async Task<GameConfigEnvironment?> FindForUpdateAsync(
        string configurationSetKey,
        string environmentName,
        CancellationToken cancellationToken = default)
    {
        var environments = await _dbContext.GameConfigEnvironments
            .FromSqlInterpolated($"""
                SELECT *
                FROM `game_config_environments`
                WHERE `ConfigurationSetKey` = {configurationSetKey}
                  AND `EnvironmentName` = {environmentName}
                LIMIT 1 FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        return environments.SingleOrDefault();
    }

    public async Task AddAsync(
        GameConfigEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        await _dbContext.GameConfigEnvironments.AddAsync(environment, cancellationToken);
    }
}
