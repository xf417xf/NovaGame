using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using NovaCore.BuildingBlocks.Correlation;
using NovaCore.Shared.Results;
using NovaGame.Application.Contracts.Operations.AdminAccess;
using NovaGame.Domain.Shared.Operations.AdminAccess;

namespace NovaGame.Api.Operations.AdminAccess;

public sealed class AdminAccessManagementExceptionFilter : IExceptionFilter
{
    private readonly ILogger<AdminAccessManagementExceptionFilter> _logger;
    private readonly ICorrelationIdAccessor _correlationIdAccessor;

    public AdminAccessManagementExceptionFilter(
        ILogger<AdminAccessManagementExceptionFilter> logger,
        ICorrelationIdAccessor correlationIdAccessor)
    {
        _logger = logger;
        _correlationIdAccessor = correlationIdAccessor;
    }

    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not AdminAccessManagementException exception)
        {
            return;
        }

        var statusCode = exception.FailureKind switch
        {
            AdminAccessFailureKind.BadRequest => StatusCodes.Status400BadRequest,
            AdminAccessFailureKind.Unauthorized => StatusCodes.Status401Unauthorized,
            AdminAccessFailureKind.Forbidden => StatusCodes.Status403Forbidden,
            AdminAccessFailureKind.NotFound => StatusCodes.Status404NotFound,
            AdminAccessFailureKind.Conflict => StatusCodes.Status409Conflict,
            AdminAccessFailureKind.UnprocessableEntity => StatusCodes.Status422UnprocessableEntity,
            AdminAccessFailureKind.InternalError => StatusCodes.Status500InternalServerError,
            _ => StatusCodes.Status500InternalServerError
        };

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(
                "Administrator management request failed with {ErrorCode} at correlation {CorrelationId}.",
                exception.Code,
                _correlationIdAccessor.CorrelationId);
        }
        else
        {
            _logger.LogWarning(
                "Administrator management request failed with {ErrorCode} at correlation {CorrelationId}.",
                exception.Code,
                _correlationIdAccessor.CorrelationId);
        }

        if (statusCode == StatusCodes.Status401Unauthorized)
        {
            context.HttpContext.Response.Headers.WWWAuthenticate = "Bearer";
        }

        context.Result = new ObjectResult(
            ApiResponse.Failure(exception.Code, exception.Message, exception.Details))
        {
            StatusCode = statusCode
        };
        context.ExceptionHandled = true;
    }
}
