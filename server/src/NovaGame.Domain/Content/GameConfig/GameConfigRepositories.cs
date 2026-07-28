namespace NovaGame.Domain.Content.GameConfig;

public enum GameConfigDraftStateFilter
{
    All = 0,
    Editable = 1,
    Validated = 2,
    Invalid = 3,
    Published = 4
}

public interface IGameConfigDraftRepository
{
    Task<GameConfigDraft?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<GameConfigDraft?> FindByIdForUpdateAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GameConfigDraft>> GetListAsync(
        string configurationSetKey,
        string? search,
        GameConfigDraftStateFilter state,
        int skipCount,
        int maxResultCount,
        CancellationToken cancellationToken = default);

    Task<long> GetCountAsync(
        string configurationSetKey,
        string? search,
        GameConfigDraftStateFilter state,
        CancellationToken cancellationToken = default);

    Task AddAsync(GameConfigDraft draft, CancellationToken cancellationToken = default);
}

public interface IPublishedGameConfigVersionRepository
{
    Task<PublishedGameConfigVersion?> FindByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<PublishedGameConfigVersion?> FindBySourceRevisionAsync(
        Guid sourceDraftId,
        long sourceDraftRevision,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PublishedGameConfigVersion>> GetListAsync(
        string configurationSetKey,
        string? schemaVersion,
        int skipCount,
        int maxResultCount,
        CancellationToken cancellationToken = default);

    Task<long> GetCountAsync(
        string configurationSetKey,
        string? schemaVersion,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        PublishedGameConfigVersion version,
        CancellationToken cancellationToken = default);
}

public interface IGameConfigEnvironmentRepository
{
    Task<GameConfigEnvironment?> FindAsync(
        string configurationSetKey,
        string environmentName,
        CancellationToken cancellationToken = default);

    Task<GameConfigEnvironment?> FindForUpdateAsync(
        string configurationSetKey,
        string environmentName,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        GameConfigEnvironment environment,
        CancellationToken cancellationToken = default);
}

public interface IGameConfigAuditRepository
{
    Task AddAsync(
        GameConfigAuditRecord auditRecord,
        CancellationToken cancellationToken = default);
}
