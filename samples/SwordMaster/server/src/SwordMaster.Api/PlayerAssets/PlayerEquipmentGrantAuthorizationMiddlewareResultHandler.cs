using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using NovaCore.Shared.Json;
using NovaCore.Shared.Results;
using SwordMaster.Application.Contracts.PlayerAssets;

namespace SwordMaster.Api.PlayerAssets;

public sealed class PlayerEquipmentGrantAuthorizationMiddlewareResultHandler :
    IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _defaultHandler = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (!IsPlayerEquipmentGrantEndpoint(context) || authorizeResult.Succeeded)
        {
            await _defaultHandler.HandleAsync(
                next,
                context,
                policy,
                authorizeResult);
            return;
        }

        var response = authorizeResult.Challenged
            ? new AuthorizationFailureResponse(
                StatusCodes.Status401Unauthorized,
                PlayerEquipmentGrantErrorCodes.UnauthorizedAdministrator,
                "An authenticated NovaGame administrator is required.")
            : authorizeResult.Forbidden
                ? new AuthorizationFailureResponse(
                    StatusCodes.Status403Forbidden,
                    PlayerEquipmentGrantErrorCodes.Forbidden,
                    "The current administrator cannot manage player equipment grants.")
                : null;
        if (response is null)
        {
            await _defaultHandler.HandleAsync(
                next,
                context,
                policy,
                authorizeResult);
            return;
        }

        context.RequestServices
            .GetRequiredService<PlayerEquipmentGrantOperationLogger>()
            .LogAuthorizationDenied(context, response.Code);

        context.Response.Clear();
        context.Response.StatusCode = response.StatusCode;
        context.Response.ContentType = "application/json";
        if (response.StatusCode == StatusCodes.Status401Unauthorized)
        {
            context.Response.Headers.WWWAuthenticate = "Bearer";
        }

        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            ApiResponse.Failure(response.Code, response.Message),
            NovaCoreJsonOptions.CreateDefault(),
            context.RequestAborted);
    }

    private static bool IsPlayerEquipmentGrantEndpoint(HttpContext context)
    {
        var actionDescriptor = context
            .GetEndpoint()?
            .Metadata
            .GetMetadata<ControllerActionDescriptor>();
        return actionDescriptor?.ControllerTypeInfo.AsType() ==
            typeof(PlayerEquipmentGrantsController);
    }

    private sealed record AuthorizationFailureResponse(
        int StatusCode,
        string Code,
        string Message);
}
