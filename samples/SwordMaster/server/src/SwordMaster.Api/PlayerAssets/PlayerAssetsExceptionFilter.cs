using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using NovaCore.Shared.Results;
using SwordMaster.Application.Contracts.PlayerAssets;

namespace SwordMaster.Api.PlayerAssets;

public sealed class PlayerAssetsExceptionFilter : IExceptionFilter
{
    private readonly PlayerAssetsOperationLogger _operationLogger;

    public PlayerAssetsExceptionFilter(PlayerAssetsOperationLogger operationLogger)
    {
        _operationLogger = operationLogger;
    }

    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not PlayerAssetsException exception)
        {
            return;
        }

        var statusCode = exception.FailureKind switch
        {
            PlayerAssetsFailureKind.BadRequest => StatusCodes.Status400BadRequest,
            PlayerAssetsFailureKind.Unauthorized => StatusCodes.Status401Unauthorized,
            PlayerAssetsFailureKind.NotFound => StatusCodes.Status404NotFound,
            PlayerAssetsFailureKind.Conflict => StatusCodes.Status409Conflict,
            PlayerAssetsFailureKind.UnprocessableEntity => StatusCodes.Status422UnprocessableEntity,
            PlayerAssetsFailureKind.InternalError => StatusCodes.Status500InternalServerError,
            _ => StatusCodes.Status500InternalServerError
        };

        _operationLogger.LogFailed(context.HttpContext, exception);

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
