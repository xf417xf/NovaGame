using Microsoft.EntityFrameworkCore;
using NovaGame.Domain.Content.GameConfig;

namespace NovaGame.Host.EntityFrameworkCore.Content.GameConfig;

public sealed class GameConfigDraftRepository : IGameConfigDraftRepository
{
    private readonly NovaGameDbContext _dbContext;

    public GameConfigDraftRepository(NovaGameDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<GameConfigDraft?> FindByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.GameConfigDrafts
            .Include(draft => draft.ValidationIssues)
            .SingleOrDefaultAsync(
            candidate => candidate.Id == id,
            cancellationToken);
    }

    public async Task<GameConfigDraft?> FindByIdForUpdateAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var idText = id.ToString();
        var drafts = await _dbContext.GameConfigDrafts
            .FromSqlInterpolated($"""
                SELECT *
                FROM `game_config_drafts`
                WHERE `Id` = {idText}
                LIMIT 1 FOR UPDATE
                """)
            .Include(draft => draft.ValidationIssues)
            .ToListAsync(cancellationToken);
        return drafts.SingleOrDefault();
    }

    public async Task<IReadOnlyList<GameConfigDraft>> GetListAsync(
        string configurationSetKey,
        string? search,
        GameConfigDraftStateFilter state,
        int skipCount,
        int maxResultCount,
        CancellationToken cancellationToken = default)
    {
        return await CreateFilteredQuery(configurationSetKey, search, state)
            .AsNoTracking()
            .OrderByDescending(draft => draft.UpdatedAtUtc)
            .ThenByDescending(draft => draft.Id)
            .Skip(skipCount)
            .Take(maxResultCount)
            .ToListAsync(cancellationToken);
    }

    public Task<long> GetCountAsync(
        string configurationSetKey,
        string? search,
        GameConfigDraftStateFilter state,
        CancellationToken cancellationToken = default)
    {
        return CreateFilteredQuery(configurationSetKey, search, state)
            .LongCountAsync(cancellationToken);
    }

    public async Task AddAsync(GameConfigDraft draft, CancellationToken cancellationToken = default)
    {
        await _dbContext.GameConfigDrafts.AddAsync(draft, cancellationToken);
    }

    private IQueryable<GameConfigDraft> CreateFilteredQuery(
        string configurationSetKey,
        string? search,
        GameConfigDraftStateFilter state)
    {
        var query = _dbContext.GameConfigDrafts
            .Where(draft => draft.ConfigurationSetKey == configurationSetKey);

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(draft =>
                draft.Name.Contains(search) || draft.SchemaVersion.Contains(search));
        }

        return state switch
        {
            GameConfigDraftStateFilter.Editable => query.Where(draft =>
                draft.PublishedVersionId == null &&
                draft.ValidationStatus == GameConfigValidationStatus.NotValidated),
            GameConfigDraftStateFilter.Validated => query.Where(draft =>
                draft.PublishedVersionId == null &&
                draft.ValidationStatus == GameConfigValidationStatus.Valid &&
                draft.LastValidatedRevision == draft.Revision),
            GameConfigDraftStateFilter.Invalid => query.Where(draft =>
                draft.PublishedVersionId == null &&
                draft.ValidationStatus == GameConfigValidationStatus.Invalid &&
                draft.LastValidatedRevision == draft.Revision),
            GameConfigDraftStateFilter.Published => query.Where(draft => draft.PublishedVersionId != null),
            _ => query
        };
    }

}
