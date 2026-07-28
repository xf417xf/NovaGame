using NovaCore.BuildingBlocks.Correlation;
using NovaGame.Application.Auditing;

namespace NovaGame.Api.Auditing;

public sealed class HttpRequestAuditContextAccessor : IRequestAuditContextAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ICorrelationIdAccessor _correlationIdAccessor;

    public HttpRequestAuditContextAccessor(
        IHttpContextAccessor httpContextAccessor,
        ICorrelationIdAccessor correlationIdAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
        _correlationIdAccessor = correlationIdAccessor;
    }

    public RequestAuditContext Current
    {
        get
        {
            var context = _httpContextAccessor.HttpContext;
            return new RequestAuditContext(
                context?.Connection.RemoteIpAddress?.ToString(),
                context?.Request.Headers.UserAgent.ToString(),
                _correlationIdAccessor.CorrelationId);
        }
    }
}
