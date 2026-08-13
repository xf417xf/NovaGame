using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NovaCore.BuildingBlocks.Correlation;
using NovaCore.BuildingBlocks.Exceptions;
using SwordMaster.Application.Contracts.PlayerAssets;
using SwordMaster.Application.PlayerAssets;

namespace SwordMaster.Api.PlayerAssets;

public sealed class PlayerEquipmentGrantOperationLogger
{
    internal const string GetPageCommandType = "GetPage";
    internal const string GrantCommandType = "Grant";
    internal const string RollbackCommandType = "Rollback";

    private const string SuccessResultCode = "Success";

    private static readonly EventId SucceededEvent = new(
        10,
        "PlayerEquipmentGrantOperationSucceeded");
    private static readonly EventId FailedEvent = new(
        11,
        "PlayerEquipmentGrantOperationFailed");
    private static readonly EventId AuthorizationDeniedEvent = new(
        12,
        "PlayerEquipmentGrantAuthorizationDenied");

    private readonly ICorrelationIdAccessor _correlationIdAccessor;
    private readonly ICurrentSwordMasterAdministrator _currentAdministrator;
    private readonly ILogger<PlayerEquipmentGrantOperationLogger> _logger;

    public PlayerEquipmentGrantOperationLogger(
        ICorrelationIdAccessor correlationIdAccessor,
        ICurrentSwordMasterAdministrator currentAdministrator,
        ILogger<PlayerEquipmentGrantOperationLogger> logger)
    {
        _correlationIdAccessor = correlationIdAccessor;
        _currentAdministrator = currentAdministrator;
        _logger = logger;
    }

    public void LogSucceeded(
        HttpContext httpContext,
        string commandType,
        Guid playerId,
        Guid? grantId,
        long revision)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandType);

        Write(
            LogLevel.Information,
            SucceededEvent,
            httpContext,
            commandType,
            playerId,
            grantId,
            SuccessResultCode,
            revision);
    }

    public void LogFailed(
        HttpContext httpContext,
        PlayerEquipmentGrantException exception)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        Write(
            exception.FailureKind == PlayerEquipmentGrantFailureKind.InternalError
                ? LogLevel.Error
                : LogLevel.Warning,
            FailedEvent,
            httpContext,
            ResolveCommandType(httpContext),
            TryGetRouteGuid(httpContext, "playerId"),
            TryGetRouteGuid(httpContext, "grantId"),
            exception.FailureKind == PlayerEquipmentGrantFailureKind.InternalError
                ? NovaCoreErrorCodes.InternalServerError
                : exception.Code,
            TryGetExpectedRevision(httpContext.Request));
    }

    public void LogUnexpectedFailure(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        Write(
            LogLevel.Error,
            FailedEvent,
            httpContext,
            ResolveCommandType(httpContext),
            TryGetRouteGuid(httpContext, "playerId"),
            TryGetRouteGuid(httpContext, "grantId"),
            NovaCoreErrorCodes.InternalServerError,
            TryGetExpectedRevision(httpContext.Request));
    }

    public void LogInvalidRequest(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        Write(
            LogLevel.Warning,
            FailedEvent,
            httpContext,
            ResolveCommandType(httpContext),
            TryGetRouteGuid(httpContext, "playerId"),
            TryGetRouteGuid(httpContext, "grantId"),
            PlayerEquipmentGrantErrorCodes.InvalidRequest,
            TryGetExpectedRevision(httpContext.Request));
    }

    public void LogAuthorizationDenied(
        HttpContext httpContext,
        string resultCode)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(resultCode);

        Write(
            LogLevel.Warning,
            AuthorizationDeniedEvent,
            httpContext,
            ResolveCommandType(httpContext),
            TryGetRouteGuid(httpContext, "playerId"),
            TryGetRouteGuid(httpContext, "grantId"),
            resultCode,
            TryGetExpectedRevision(httpContext.Request));
    }

    private void Write(
        LogLevel logLevel,
        EventId eventId,
        HttpContext httpContext,
        string commandType,
        Guid? playerId,
        Guid? grantId,
        string resultCode,
        long? revision)
    {
        _logger.Log(
            logLevel,
            eventId,
            "Player equipment grant operation completed. CorrelationId={CorrelationId} AdministratorId={AdministratorId} PlayerId={PlayerId} GrantId={GrantId} CommandType={CommandType} ResultCode={ResultCode} Revision={Revision}.",
            _correlationIdAccessor.CorrelationId ?? httpContext.TraceIdentifier,
            _currentAdministrator.AdministratorId,
            playerId,
            grantId,
            commandType,
            resultCode,
            revision);
    }

    private static string ResolveCommandType(HttpContext httpContext)
    {
        if (HttpMethods.IsGet(httpContext.Request.Method))
        {
            return GetPageCommandType;
        }

        if (HttpMethods.IsPost(httpContext.Request.Method))
        {
            return httpContext.Request.RouteValues.ContainsKey("grantId")
                ? RollbackCommandType
                : GrantCommandType;
        }

        return httpContext.Request.Method;
    }

    private static Guid? TryGetRouteGuid(
        HttpContext httpContext,
        string routeValueName)
    {
        if (!httpContext.Request.RouteValues.TryGetValue(routeValueName, out var value) ||
            !Guid.TryParse(
                Convert.ToString(value, CultureInfo.InvariantCulture),
                out var identifier) ||
            identifier == Guid.Empty)
        {
            return null;
        }

        return identifier;
    }

    private static long? TryGetExpectedRevision(HttpRequest request)
    {
        try
        {
            var entityTags = request.GetTypedHeaders().IfMatch;
            if (entityTags is null || entityTags.Count != 1)
            {
                return null;
            }

            var entityTag = entityTags[0];
            var tag = entityTag.Tag.Value;
            if (entityTag.IsWeak ||
                string.IsNullOrEmpty(tag) ||
                tag.Length < 3 ||
                tag[0] != '"' ||
                tag[^1] != '"')
            {
                return null;
            }

            var revisionText = tag.AsSpan(1, tag.Length - 2);
            return long.TryParse(
                    revisionText,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var revision) &&
                revision > 0 &&
                revisionText.SequenceEqual(
                    revision.ToString(CultureInfo.InvariantCulture).AsSpan())
                    ? revision
                    : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
