using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NovaCore.BuildingBlocks.Correlation;
using SwordMaster.Application.Contracts.PlayerAssets;
using SwordMaster.Application.PlayerAssets;

namespace SwordMaster.Api.PlayerAssets;

public sealed class PlayerAssetsOperationLogger
{
    internal const string EquipCommandType = "Equip";
    internal const string UnequipCommandType = "Unequip";
    internal const string GetAttributesCommandType = "GetAttributes";

    private const string SuccessResultCode = "Success";

    private static readonly EventId SucceededEvent = new(
        1,
        "PlayerAssetsOperationSucceeded");
    private static readonly EventId FailedEvent = new(
        2,
        "PlayerAssetsOperationFailed");

    private readonly ICorrelationIdAccessor _correlationIdAccessor;
    private readonly ICurrentPlayerContext _currentPlayer;
    private readonly ILogger<PlayerAssetsOperationLogger> _logger;

    public PlayerAssetsOperationLogger(
        ICorrelationIdAccessor correlationIdAccessor,
        ICurrentPlayerContext currentPlayer,
        ILogger<PlayerAssetsOperationLogger> logger)
    {
        _correlationIdAccessor = correlationIdAccessor;
        _currentPlayer = currentPlayer;
        _logger = logger;
    }

    public void LogSucceeded(
        HttpContext httpContext,
        string commandType,
        Guid characterId,
        long revision)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandType);

        Write(
            LogLevel.Information,
            SucceededEvent,
            httpContext,
            commandType,
            characterId,
            SuccessResultCode,
            revision,
            exception: null);
    }

    public void LogFailed(HttpContext httpContext, PlayerAssetsException exception)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        var isInternalError = exception.FailureKind == PlayerAssetsFailureKind.InternalError;
        Write(
            isInternalError ? LogLevel.Error : LogLevel.Warning,
            FailedEvent,
            httpContext,
            ResolveCommandType(httpContext.Request.Method),
            TryGetCharacterId(httpContext),
            exception.Code,
            TryGetExpectedRevision(httpContext.Request),
            isInternalError ? exception : null);
    }

    private void Write(
        LogLevel logLevel,
        EventId eventId,
        HttpContext httpContext,
        string commandType,
        Guid? characterId,
        string resultCode,
        long? revision,
        Exception? exception)
    {
        _logger.Log(
            logLevel,
            eventId,
            exception,
            "PlayerAssets operation completed. CorrelationId={CorrelationId} PlayerId={PlayerId} CharacterId={CharacterId} CommandType={CommandType} ResultCode={ResultCode} Revision={Revision}.",
            _correlationIdAccessor.CorrelationId ?? httpContext.TraceIdentifier,
            _currentPlayer.PlayerId,
            characterId,
            commandType,
            resultCode,
            revision);
    }

    private static string ResolveCommandType(string httpMethod)
    {
        return httpMethod switch
        {
            "PUT" => EquipCommandType,
            "DELETE" => UnequipCommandType,
            "GET" => GetAttributesCommandType,
            _ => httpMethod
        };
    }

    private static Guid? TryGetCharacterId(HttpContext httpContext)
    {
        if (!httpContext.Request.RouteValues.TryGetValue("characterId", out var value) ||
            !Guid.TryParse(
                Convert.ToString(value, CultureInfo.InvariantCulture),
                out var characterId))
        {
            return null;
        }

        return characterId;
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
