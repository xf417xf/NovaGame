using NovaCore.BuildingBlocks.Domain;

namespace NovaGame.Domain.Operations.AdminAccess;

public sealed class AdminAccessAuditRecord : AggregateRoot<Guid>
{
    private AdminAccessAuditRecord()
    {
    }

    public AdminAccessAuditRecord(
        Guid id,
        AdminAccessAuditAction action,
        DateTime occurredAtUtc,
        bool succeeded,
        Guid? administratorId = null,
        Guid? actorAdministratorId = null,
        Guid? roleId = null,
        string? subjectUserName = null,
        string? permissionName = null,
        string? errorCode = null,
        string? ipAddress = null,
        string? userAgent = null,
        string? correlationId = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Audit record ID cannot be empty.", nameof(id));
        }

        if (occurredAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The audit timestamp must use UTC.", nameof(occurredAtUtc));
        }

        Id = id;
        Action = action;
        OccurredAtUtc = occurredAtUtc;
        Succeeded = succeeded;
        AdministratorId = administratorId;
        ActorAdministratorId = actorAdministratorId;
        RoleId = roleId;
        SubjectUserName = NormalizeMetadata(subjectUserName, 64);
        PermissionName = NormalizeMetadata(permissionName, 128);
        ErrorCode = NormalizeMetadata(errorCode, 128);
        IpAddress = NormalizeMetadata(ipAddress, 64);
        UserAgent = NormalizeMetadata(userAgent, 512);
        CorrelationId = NormalizeMetadata(correlationId, 128);
    }

    public AdminAccessAuditAction Action { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    public bool Succeeded { get; private set; }

    public Guid? AdministratorId { get; private set; }

    public Guid? ActorAdministratorId { get; private set; }

    public Guid? RoleId { get; private set; }

    public string? SubjectUserName { get; private set; }

    public string? PermissionName { get; private set; }

    public string? ErrorCode { get; private set; }

    public string? IpAddress { get; private set; }

    public string? UserAgent { get; private set; }

    public string? CorrelationId { get; private set; }

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
