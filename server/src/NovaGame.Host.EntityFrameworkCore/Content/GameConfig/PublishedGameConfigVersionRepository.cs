using Microsoft.EntityFrameworkCore;
using NovaGame.Domain.Content.GameConfig;

namespace NovaGame.Host.EntityFrameworkCore.Content.GameConfig;

public sealed class PublishedGameConfigVersionRepository : IPublishedGameConfigVersionRepository
{
    private readonly NovaGameDbContext _dbContext;

    public PublishedGameConfigVersionRepository(NovaGameDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<PublishedGameConfigVersion?> FindByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.PublishedGameConfigVersions.SingleOrDefaultAsync(
            version => version.Id == id,
            cancellationToken);
    }

    public Task<PublishedGameConfigVersion?> FindBySourceRevisionAsync(
        Guid sourceDraftId,
        long sourceDraftRevision,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.PublishedGameConfigVersions.SingleOrDefaultAsync(
            version =>
                version.SourceDraftId == sourceDraftId &&
                version.SourceDraftRevision == sourceDraftRevision,
            cancellationToken);
    }

    public async Task<IReadOnlyList<PublishedGameConfigVersion>> GetListAsync(
        string configurationSetKey,
        string? schemaVersion,
        int skipCount,
        int maxResultCount,
        CancellationToken cancellationToken = default)
    {
        return await CreateFilteredQuery(configurationSetKey, schemaVersion)
            .AsNoTracking()
            .OrderByDescending(version => version.PublishedAtUtc)
            .ThenByDescending(version => version.Id)
            .Skip(skipCount)
            .Take(maxResultCount)
            .ToListAsync(cancellationToken);
    }

    public Task<long> GetCountAsync(
        string configurationSetKey,
        string? schemaVersion,
        CancellationToken cancellationToken = default)
    {
        return CreateFilteredQuery(configurationSetKey, schemaVersion)
            .LongCountAsync(cancellationToken);
    }

    public async Task AddAsync(
        PublishedGameConfigVersion version,
        CancellationToken cancellationToken = default)
    {
        await _dbContext.PublishedGameConfigVersions.AddAsync(version, cancellationToken);
    }

    private IQueryable<PublishedGameConfigVersion> CreateFilteredQuery(
        string configurationSetKey,
        string? schemaVersion)
    {
        var query = _dbContext.PublishedGameConfigVersions
            .Where(version => version.ConfigurationSetKey == configurationSetKey);

        if (!string.IsNullOrWhiteSpace(schemaVersion))
        {
            query = query.Where(version => version.SchemaVersion == schemaVersion);
        }

        return query;
    }
}
