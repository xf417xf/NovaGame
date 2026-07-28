using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using NovaCore.Shared.Results;
using NovaGame.Application.Contracts.Content.GameConfig;

namespace NovaGame.Api.Content.GameConfig;

public sealed class GameConfigExceptionFilter : IExceptionFilter
{
    private readonly ILogger<GameConfigExceptionFilter> _logger;

    public GameConfigExceptionFilter(ILogger<GameConfigExceptionFilter> logger)
    {
        _logger = logger;
    }

    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not GameConfigException exception)
        {
            return;
        }

        var statusCode = exception.FailureKind switch
        {
            GameConfigFailureKind.BadRequest => StatusCodes.Status400BadRequest,
            GameConfigFailureKind.Unauthorized => StatusCodes.Status401Unauthorized,
            GameConfigFailureKind.Forbidden => StatusCodes.Status403Forbidden,
            GameConfigFailureKind.NotFound => StatusCodes.Status404NotFound,
            GameConfigFailureKind.Conflict => StatusCodes.Status409Conflict,
            GameConfigFailureKind.UnprocessableEntity => StatusCodes.Status422UnprocessableEntity,
            GameConfigFailureKind.InternalError => StatusCodes.Status500InternalServerError,
            _ => StatusCodes.Status500InternalServerError
        };

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(exception, "GameConfig request failed with {ErrorCode}.", exception.Code);
        }
        else
        {
            _logger.LogWarning(exception, "GameConfig request failed with {ErrorCode}.", exception.Code);
        }

        context.Result = new ObjectResult(
            ApiResponse.Failure(exception.Code, exception.Message, exception.Details))
        {
            StatusCode = statusCode
        };
        context.ExceptionHandled = true;
    }
}
