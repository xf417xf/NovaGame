namespace NovaGame.Domain.Operations.AdminAccess;

public interface IRefreshTokenSessionRepository
{
    Task<RefreshTokenSession?> FindByTokenHashAsync(
        byte[] tokenHash,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RefreshTokenSession>> GetFamilyForUpdateAsync(
        Guid familyId,
        CancellationToken cancellationToken = default);

    Task AddAsync(RefreshTokenSession session, CancellationToken cancellationToken = default);
}
