namespace NovaGame.Domain.Operations.AdminAccess;

public interface IAdministratorRepository
{
    Task<bool> AnyAsync(CancellationToken cancellationToken = default);

    Task<Administrator?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Administrator?> FindByIdForUpdateAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<Administrator?> FindByNormalizedUserNameAsync(
        string normalizedUserName,
        CancellationToken cancellationToken = default);

    Task<Administrator?> FindByNormalizedUserNameForUpdateAsync(
        string normalizedUserName,
        CancellationToken cancellationToken = default);

    Task<AdministratorAuthorizationSnapshot?> GetAuthorizationSnapshotAsync(
        Guid administratorId,
        CancellationToken cancellationToken = default);

    Task AddAsync(Administrator administrator, CancellationToken cancellationToken = default);
}
