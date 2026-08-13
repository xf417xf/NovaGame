using NovaCore.BuildingBlocks.Domain;

namespace SwordMaster.Domain.PlayerAssets;

public enum PlayerEquipmentGrantStatus
{
    Active = 1,
    RolledBack = 2
}

public sealed class PlayerEquipmentGrant : AggregateRoot<Guid>
{
    public const int ReasonMaximumLength = 500;
    public const int RequestFingerprintLength = 64;
    public const int IpAddressMaximumLength = 64;
    public const int UserAgentMaximumLength = 512;
    public const int CorrelationIdMaximumLength = 128;

    private PlayerEquipmentGrant()
    {
        OwnedItemId = null!;
    }

    public PlayerEquipmentGrant(
        Guid grantId,
        PlayerId playerId,
        OwnedItemId ownedItemId,
        string configuredItemId,
        Guid configurationVersionId,
        Guid grantedByAdministratorId,
        DateTime grantedAtUtc,
        string grantReason,
        Guid grantIdempotencyKey,
        string grantRequestFingerprint,
        long grantResultingRevision,
        string? ipAddress = null,
        string? userAgent = null,
        string? correlationId = null)
    {
        EnsureNotEmpty(grantId, nameof(grantId));
        ArgumentNullException.ThrowIfNull(playerId);
        ArgumentNullException.ThrowIfNull(ownedItemId);
        EnsureNotEmpty(configurationVersionId, nameof(configurationVersionId));
        EnsureNotEmpty(grantedByAdministratorId, nameof(grantedByAdministratorId));
        EnsureNotEmpty(grantIdempotencyKey, nameof(grantIdempotencyKey));
        EnsurePositiveRevision(grantResultingRevision, nameof(grantResultingRevision));

        Id = grantId;
        PlayerIdValue = playerId.Value;
        OwnedItemId = ownedItemId;
        ConfiguredItemId = EquipmentDefinition.NormalizeConfiguredItemId(configuredItemId);
        ConfigurationVersionId = configurationVersionId;
        Status = PlayerEquipmentGrantStatus.Active;
        GrantedByAdministratorId = grantedByAdministratorId;
        GrantedAtUtc = EnsureUtc(grantedAtUtc, nameof(grantedAtUtc));
        GrantReason = NormalizeReason(grantReason, nameof(grantReason));
        GrantIdempotencyKey = grantIdempotencyKey;
        GrantRequestFingerprint = NormalizeFingerprint(
            grantRequestFingerprint,
            nameof(grantRequestFingerprint));
        GrantResultingRevision = grantResultingRevision;
        GrantIpAddress = NormalizeMetadata(ipAddress, IpAddressMaximumLength);
        GrantUserAgent = NormalizeMetadata(userAgent, UserAgentMaximumLength);
        GrantCorrelationId = NormalizeMetadata(correlationId, CorrelationIdMaximumLength);
    }

    public Guid GrantId => Id;

    private Guid PlayerIdValue { get; set; }

    public PlayerId PlayerId =>
        global::SwordMaster.Domain.PlayerAssets.PlayerId.From(PlayerIdValue);

    public OwnedItemId OwnedItemId { get; private set; }

    public string ConfiguredItemId { get; private set; } = string.Empty;

    public Guid ConfigurationVersionId { get; private set; }

    public PlayerEquipmentGrantStatus Status { get; private set; }

    public Guid GrantedByAdministratorId { get; private set; }

    public DateTime GrantedAtUtc { get; private set; }

    public string GrantReason { get; private set; } = string.Empty;

    public Guid GrantIdempotencyKey { get; private set; }

    public string GrantRequestFingerprint { get; private set; } = string.Empty;

    public long GrantResultingRevision { get; private set; }

    public string? GrantIpAddress { get; private set; }

    public string? GrantUserAgent { get; private set; }

    public string? GrantCorrelationId { get; private set; }

    public Guid? RolledBackByAdministratorId { get; private set; }

    public DateTime? RolledBackAtUtc { get; private set; }

    public string? RollbackReason { get; private set; }

    public Guid? RollbackIdempotencyKey { get; private set; }

    public string? RollbackRequestFingerprint { get; private set; }

    public long? RollbackResultingRevision { get; private set; }

    public string? RollbackIpAddress { get; private set; }

    public string? RollbackUserAgent { get; private set; }

    public string? RollbackCorrelationId { get; private set; }

    public bool MatchesGrantRequest(string requestFingerprint)
    {
        return string.Equals(
            GrantRequestFingerprint,
            requestFingerprint,
            StringComparison.Ordinal);
    }

    public bool MatchesRollbackRequest(string requestFingerprint)
    {
        return string.Equals(
            RollbackRequestFingerprint,
            requestFingerprint,
            StringComparison.Ordinal);
    }

    public void Rollback(
        Guid rolledBackByAdministratorId,
        DateTime rolledBackAtUtc,
        string rollbackReason,
        Guid rollbackIdempotencyKey,
        string rollbackRequestFingerprint,
        long rollbackResultingRevision,
        string? ipAddress = null,
        string? userAgent = null,
        string? correlationId = null)
    {
        if (Status != PlayerEquipmentGrantStatus.Active)
        {
            throw new PlayerAssetsDomainException(
                PlayerAssetsErrorCodes.EquipmentGrantAlreadyRolledBack,
                "The equipment grant has already been rolled back.");
        }

        EnsureNotEmpty(rolledBackByAdministratorId, nameof(rolledBackByAdministratorId));
        EnsureNotEmpty(rollbackIdempotencyKey, nameof(rollbackIdempotencyKey));
        EnsurePositiveRevision(rollbackResultingRevision, nameof(rollbackResultingRevision));
        var normalizedTime = EnsureUtc(rolledBackAtUtc, nameof(rolledBackAtUtc));
        if (normalizedTime < GrantedAtUtc)
        {
            throw new ArgumentException(
                "The rollback timestamp cannot precede the grant timestamp.",
                nameof(rolledBackAtUtc));
        }

        var normalizedReason = NormalizeReason(rollbackReason, nameof(rollbackReason));
        var normalizedFingerprint = NormalizeFingerprint(
            rollbackRequestFingerprint,
            nameof(rollbackRequestFingerprint));
        var normalizedIpAddress = NormalizeMetadata(ipAddress, IpAddressMaximumLength);
        var normalizedUserAgent = NormalizeMetadata(userAgent, UserAgentMaximumLength);
        var normalizedCorrelationId = NormalizeMetadata(
            correlationId,
            CorrelationIdMaximumLength);

        Status = PlayerEquipmentGrantStatus.RolledBack;
        RolledBackByAdministratorId = rolledBackByAdministratorId;
        RolledBackAtUtc = normalizedTime;
        RollbackReason = normalizedReason;
        RollbackIdempotencyKey = rollbackIdempotencyKey;
        RollbackRequestFingerprint = normalizedFingerprint;
        RollbackResultingRevision = rollbackResultingRevision;
        RollbackIpAddress = normalizedIpAddress;
        RollbackUserAgent = normalizedUserAgent;
        RollbackCorrelationId = normalizedCorrelationId;
    }

    private static void EnsureNotEmpty(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifiers cannot be empty.", parameterName);
        }
    }

    private static void EnsurePositiveRevision(long value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Resulting revisions must be positive.");
        }
    }

    private static DateTime EnsureUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Operation timestamps must use UTC.",
                parameterName);
        }

        return value;
    }

    private static string NormalizeReason(string value, string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized) ||
            normalized.Length > ReasonMaximumLength ||
            normalized.Any(char.IsControl))
        {
            throw new ArgumentException(
                $"Reasons must contain 1 to {ReasonMaximumLength} non-control characters.",
                parameterName);
        }

        return normalized;
    }

    private static string NormalizeFingerprint(string value, string parameterName)
    {
        if (value is null ||
            value.Length != RequestFingerprintLength ||
            value.Any(character => character is not (>= '0' and <= '9') and not (>= 'A' and <= 'F')))
        {
            throw new ArgumentException(
                "Request fingerprints must be uppercase 64-character SHA-256 hexadecimal strings.",
                parameterName);
        }

        return value;
    }

    private static string? NormalizeMetadata(string? value, int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength];
    }
}
