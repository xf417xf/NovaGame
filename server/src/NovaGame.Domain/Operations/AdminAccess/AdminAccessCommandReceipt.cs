using System.Security.Cryptography;
using NovaCore.BuildingBlocks.Domain;

namespace NovaGame.Domain.Operations.AdminAccess;

public enum AdminAccessCommandKind
{
    AdministratorCreate = 1,
    AdminRoleCreate = 2,
    AdministratorPasswordReset = 3
}

public sealed class AdminAccessCommandReceipt : AggregateRoot<Guid>
{
    private byte[] _requestFingerprint = [];

    private AdminAccessCommandReceipt()
    {
    }

    public AdminAccessCommandReceipt(
        Guid id,
        Guid actorAdministratorId,
        AdminAccessCommandKind commandKind,
        Guid idempotencyKey,
        byte[] requestFingerprint,
        Guid targetResourceId,
        long resultingRevision,
        DateTime createdAtUtc)
    {
        if (id == Guid.Empty || actorAdministratorId == Guid.Empty ||
            idempotencyKey == Guid.Empty || targetResourceId == Guid.Empty)
        {
            throw new ArgumentException("Command-receipt identifiers cannot be empty.");
        }

        if (!Enum.IsDefined(commandKind))
        {
            throw new ArgumentOutOfRangeException(nameof(commandKind));
        }

        if (requestFingerprint is not { Length: 32 })
        {
            throw new ArgumentException(
                "Command request fingerprints must contain 32 bytes.",
                nameof(requestFingerprint));
        }

        if (resultingRevision <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(resultingRevision),
                "Resulting revisions must be positive.");
        }

        if (createdAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The receipt timestamp must use UTC.", nameof(createdAtUtc));
        }

        Id = id;
        ActorAdministratorId = actorAdministratorId;
        CommandKind = commandKind;
        IdempotencyKey = idempotencyKey;
        _requestFingerprint = requestFingerprint.ToArray();
        TargetResourceId = targetResourceId;
        ResultingRevision = resultingRevision;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid ActorAdministratorId { get; private set; }

    public AdminAccessCommandKind CommandKind { get; private set; }

    public Guid IdempotencyKey { get; private set; }

    public byte[] RequestFingerprint
    {
        get => _requestFingerprint.ToArray();
        private set => _requestFingerprint = value.ToArray();
    }

    public Guid TargetResourceId { get; private set; }

    public long ResultingRevision { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public bool MatchesFingerprint(ReadOnlySpan<byte> requestFingerprint)
    {
        return requestFingerprint.Length == _requestFingerprint.Length &&
            CryptographicOperations.FixedTimeEquals(_requestFingerprint, requestFingerprint);
    }
}
