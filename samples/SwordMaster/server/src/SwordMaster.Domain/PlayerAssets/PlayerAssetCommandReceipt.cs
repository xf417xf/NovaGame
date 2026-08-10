namespace SwordMaster.Domain.PlayerAssets;

public sealed class PlayerAssetCommandReceipt
{
    public const int RequestFingerprintMaximumLength = 256;

    private PlayerAssetCommandReceipt()
    {
        PlayerId = null!;
    }

    public PlayerAssetCommandReceipt(
        PlayerId playerId,
        Guid idempotencyKey,
        string requestFingerprint,
        long resultingRevision,
        DateTime completedAtUtc)
    {
        PlayerId = playerId ?? throw new ArgumentNullException(nameof(playerId));
        if (idempotencyKey == Guid.Empty)
        {
            throw new ArgumentException("Idempotency keys cannot be empty.", nameof(idempotencyKey));
        }

        var normalizedFingerprint = requestFingerprint?.Trim();
        if (string.IsNullOrEmpty(normalizedFingerprint) ||
            normalizedFingerprint.Length > RequestFingerprintMaximumLength ||
            normalizedFingerprint.Any(char.IsControl))
        {
            throw new ArgumentException(
                $"Request fingerprints must contain 1 to {RequestFingerprintMaximumLength} non-control characters.",
                nameof(requestFingerprint));
        }

        if (resultingRevision <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(resultingRevision),
                "Resulting revisions must be positive.");
        }

        if (completedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Receipt timestamps must use UTC.", nameof(completedAtUtc));
        }

        IdempotencyKey = idempotencyKey;
        RequestFingerprint = normalizedFingerprint;
        ResultingRevision = resultingRevision;
        CompletedAtUtc = completedAtUtc;
    }

    public PlayerId PlayerId { get; private set; }

    public Guid IdempotencyKey { get; private set; }

    public string RequestFingerprint { get; private set; } = string.Empty;

    public long ResultingRevision { get; private set; }

    public DateTime CompletedAtUtc { get; private set; }

    public bool MatchesFingerprint(string requestFingerprint)
    {
        return string.Equals(RequestFingerprint, requestFingerprint?.Trim(), StringComparison.Ordinal);
    }
}

public interface IPlayerAssetCommandReceiptRepository
{
    Task<PlayerAssetCommandReceipt?> FindAsync(
        PlayerId playerId,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<PlayerAssetCommandReceipt?> FindForUpdateAsync(
        PlayerId playerId,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        PlayerAssetCommandReceipt receipt,
        CancellationToken cancellationToken = default);
}
