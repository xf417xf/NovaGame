using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using NovaCore.BuildingBlocks.Exceptions;
using NovaCore.Shared.Results;
using SwordMaster.Application.Contracts.PlayerAssets;

namespace SwordMaster.Api.PlayerAssets;

public sealed class PlayerEquipmentGrantExceptionFilter : IExceptionFilter
{
    private const string InternalErrorMessage = "An internal server error occurred.";

    private readonly PlayerEquipmentGrantOperationLogger _operationLogger;

    public PlayerEquipmentGrantExceptionFilter(
        PlayerEquipmentGrantOperationLogger operationLogger)
    {
        _operationLogger = operationLogger;
    }

    public void OnException(ExceptionContext context)
    {
        if (!PlayerEquipmentGrantEndpoint.IsController(context.ActionDescriptor))
        {
            return;
        }

        if (context.Exception is OperationCanceledException &&
            context.HttpContext.RequestAborted.IsCancellationRequested)
        {
            return;
        }

        if (context.Exception is PlayerEquipmentGrantException exception)
        {
            HandleKnownException(context, exception);
            return;
        }

        _operationLogger.LogUnexpectedFailure(context.HttpContext);
        context.Result = new ObjectResult(
            ApiResponse.Failure(
                NovaCoreErrorCodes.InternalServerError,
                InternalErrorMessage))
        {
            StatusCode = StatusCodes.Status500InternalServerError
        };
        context.ExceptionHandled = true;
    }

    private void HandleKnownException(
        ExceptionContext context,
        PlayerEquipmentGrantException exception)
    {
        var statusCode = exception.FailureKind switch
        {
            PlayerEquipmentGrantFailureKind.BadRequest =>
                StatusCodes.Status400BadRequest,
            PlayerEquipmentGrantFailureKind.Unauthorized =>
                StatusCodes.Status401Unauthorized,
            PlayerEquipmentGrantFailureKind.Forbidden =>
                StatusCodes.Status403Forbidden,
            PlayerEquipmentGrantFailureKind.NotFound =>
                StatusCodes.Status404NotFound,
            PlayerEquipmentGrantFailureKind.Conflict =>
                StatusCodes.Status409Conflict,
            PlayerEquipmentGrantFailureKind.UnprocessableEntity =>
                StatusCodes.Status422UnprocessableEntity,
            PlayerEquipmentGrantFailureKind.InternalError =>
                StatusCodes.Status500InternalServerError,
            _ => StatusCodes.Status500InternalServerError
        };

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            HandleInternalError(context);
            return;
        }

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

    private void HandleInternalError(ExceptionContext context)
    {
        _operationLogger.LogUnexpectedFailure(context.HttpContext);
        context.Result = new ObjectResult(
            ApiResponse.Failure(
                NovaCoreErrorCodes.InternalServerError,
                InternalErrorMessage))
        {
            StatusCode = StatusCodes.Status500InternalServerError
        };
        context.ExceptionHandled = true;
    }
}

public sealed class PlayerEquipmentGrantUnsupportedMediaTypeResultFilter :
    IAlwaysRunResultFilter
{
    private readonly PlayerEquipmentGrantOperationLogger _operationLogger;

    public PlayerEquipmentGrantUnsupportedMediaTypeResultFilter(
        PlayerEquipmentGrantOperationLogger operationLogger)
    {
        _operationLogger = operationLogger;
    }

    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (!PlayerEquipmentGrantEndpoint.IsController(context.ActionDescriptor) ||
            context.Result is not IStatusCodeActionResult
            {
                StatusCode: StatusCodes.Status415UnsupportedMediaType
            })
        {
            return;
        }

        _operationLogger.LogInvalidRequest(context.HttpContext);
        context.Result = new BadRequestObjectResult(
            ApiResponse.Failure(
                PlayerEquipmentGrantErrorCodes.InvalidRequest,
                "The request content type is not supported."));
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
    }
}

internal static class PlayerEquipmentGrantEndpoint
{
    public static bool IsController(Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor descriptor)
    {
        return descriptor is ControllerActionDescriptor actionDescriptor &&
            actionDescriptor.ControllerTypeInfo.AsType() ==
                typeof(PlayerEquipmentGrantsController);
    }
}
