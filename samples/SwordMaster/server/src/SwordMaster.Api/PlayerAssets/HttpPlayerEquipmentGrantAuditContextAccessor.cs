using Microsoft.AspNetCore.Http;
using NovaCore.BuildingBlocks.Correlation;
using SwordMaster.Application.PlayerAssets;

namespace SwordMaster.Api.PlayerAssets;

public sealed class HttpPlayerEquipmentGrantAuditContextAccessor :
    IPlayerEquipmentGrantAuditContextAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ICorrelationIdAccessor _correlationIdAccessor;

    public HttpPlayerEquipmentGrantAuditContextAccessor(
        IHttpContextAccessor httpContextAccessor,
        ICorrelationIdAccessor correlationIdAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
        _correlationIdAccessor = correlationIdAccessor;
    }

    public PlayerEquipmentGrantAuditContext Current
    {
        get
        {
            var context = _httpContextAccessor.HttpContext;
            return new PlayerEquipmentGrantAuditContext(
                context?.Connection.RemoteIpAddress?.ToString(),
                context?.Request.Headers.UserAgent.ToString(),
                _correlationIdAccessor.CorrelationId);
        }
    }
}
