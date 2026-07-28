using NovaCore.BuildingBlocks.Domain;

namespace NovaGame.Domain.Operations.AdminAccess;

public sealed class RefreshTokenSession : AggregateRoot<Guid>
{
    private RefreshTokenSession()
    {
    }

    public RefreshTokenSession(
        Guid id,
        Guid administratorId,
        Guid familyId,
        byte[] tokenHash,
        DateTime createdAtUtc,
        DateTime expiresAtUtc,
        string? createdFromIpAddress)
    {
        if (id == Guid.Empty || administratorId == Guid.Empty || familyId == Guid.Empty)
        {
            throw new ArgumentException("Refresh-token identifiers cannot be empty.");
        }

        if (tokenHash is not { Length: 32 })
        {
            throw new ArgumentException("Refresh-token hashes must contain 32 bytes.", nameof(tokenHash));
        }

        if (createdAtUtc.Kind != DateTimeKind.Utc || expiresAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Refresh-token timestamps must use UTC.");
        }

        if (expiresAtUtc <= createdAtUtc)
        {
            throw new ArgumentException("Refresh-token expiry must be later than creation time.", nameof(expiresAtUtc));
        }

        Id = id;
        AdministratorId = administratorId;
        FamilyId = familyId;
        TokenHash = tokenHash.ToArray();
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        CreatedFromIpAddress = NormalizeMetadata(createdFromIpAddress, 64);
    }

    public Guid AdministratorId { get; private set; }

    public Guid FamilyId { get; private set; }

    public byte[] TokenHash { get; private set; } = [];

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime ExpiresAtUtc { get; private set; }

    public string? CreatedFromIpAddress { get; private set; }

    public DateTime? RevokedAtUtc { get; private set; }

    public RefreshTokenRevocationReason? RevocationReason { get; private set; }

    public string? RevokedFromIpAddress { get; private set; }

    public Guid? ReplacedBySessionId { get; private set; }

    public bool IsExpired(DateTime nowUtc)
    {
        return nowUtc >= ExpiresAtUtc;
    }

    public bool IsRevoked => RevokedAtUtc.HasValue;

    public bool WasRotated => RevocationReason == RefreshTokenRevocationReason.Rotated && ReplacedBySessionId.HasValue;

    public void Rotate(Guid replacementSessionId, DateTime revokedAtUtc, string? revokedFromIpAddress)
    {
        if (replacementSessionId == Guid.Empty)
        {
            throw new ArgumentException("Replacement session ID cannot be empty.", nameof(replacementSessionId));
        }

        Revoke(RefreshTokenRevocationReason.Rotated, revokedAtUtc, revokedFromIpAddress);
        ReplacedBySessionId = replacementSessionId;
    }

    public void Revoke(
        RefreshTokenRevocationReason reason,
        DateTime revokedAtUtc,
        string? revokedFromIpAddress)
    {
        if (IsRevoked)
        {
            return;
        }

        if (revokedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The revocation timestamp must use UTC.", nameof(revokedAtUtc));
        }

        RevokedAtUtc = revokedAtUtc;
        RevocationReason = reason;
        RevokedFromIpAddress = NormalizeMetadata(revokedFromIpAddress, 64);
    }

    private static string? NormalizeMetadata(string? value, int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }
}
