namespace NovaGame.Application.Auditing;

public interface IRequestAuditContextAccessor
{
    RequestAuditContext Current { get; }
}

public sealed record RequestAuditContext(
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId);
