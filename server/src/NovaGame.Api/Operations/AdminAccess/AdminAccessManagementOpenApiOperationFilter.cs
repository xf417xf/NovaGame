using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace NovaGame.Api.Operations.AdminAccess;

public sealed class AdminAccessManagementOpenApiOperationFilter : IOperationFilter
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";
    private const string IfMatchHeader = "If-Match";

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.MethodInfo.DeclaringType != typeof(AdminAccessManagementController))
        {
            return;
        }

        if (RequiresIdempotencyKey(context.MethodInfo.Name))
        {
            AddRequiredHeader(
                operation,
                IdempotencyKeyHeader,
                "Canonical hyphenated GUID used to safely replay this command.");
        }

        if (RequiresIfMatch(context.MethodInfo.Name))
        {
            AddRequiredHeader(
                operation,
                IfMatchHeader,
                "Strong ETag containing the current positive aggregate revision, for example \"1\".");
        }

        if (ReturnsEntityTag(context.MethodInfo.Name) &&
            operation.Responses.TryGetValue(StatusCodes.Status200OK.ToString(), out var response))
        {
            response.Headers ??= new Dictionary<string, OpenApiHeader>(StringComparer.Ordinal);
            response.Headers["ETag"] = new OpenApiHeader
            {
                Description = "Strong ETag containing the returned positive aggregate revision.",
                Schema = new OpenApiSchema
                {
                    Type = "string"
                }
            };
        }

        if (operation.Responses.TryGetValue(StatusCodes.Status401Unauthorized.ToString(), out var unauthorized))
        {
            unauthorized.Headers ??= new Dictionary<string, OpenApiHeader>(StringComparer.Ordinal);
            unauthorized.Headers["WWW-Authenticate"] = new OpenApiHeader
            {
                Description = "Bearer authentication challenge.",
                Schema = new OpenApiSchema
                {
                    Type = "string"
                }
            };
        }
    }

    private static void AddRequiredHeader(
        OpenApiOperation operation,
        string name,
        string description)
    {
        operation.Parameters ??= [];
        if (operation.Parameters.Any(parameter =>
                parameter.In == ParameterLocation.Header &&
                string.Equals(parameter.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = name,
            In = ParameterLocation.Header,
            Required = true,
            Description = description,
            Schema = new OpenApiSchema
            {
                Type = "string"
            }
        });
    }

    private static bool RequiresIdempotencyKey(string actionName)
    {
        return actionName is nameof(AdminAccessManagementController.CreateAdministratorAsync) or
            nameof(AdminAccessManagementController.CreateAdminRoleAsync) or
            nameof(AdminAccessManagementController.ResetAdministratorPasswordAsync);
    }

    private static bool RequiresIfMatch(string actionName)
    {
        return actionName is nameof(AdminAccessManagementController.ChangeAdministratorDisplayNameAsync) or
            nameof(AdminAccessManagementController.ReplaceAdministratorRolesAsync) or
            nameof(AdminAccessManagementController.SetAdministratorActiveStateAsync) or
            nameof(AdminAccessManagementController.ResetAdministratorPasswordAsync) or
            nameof(AdminAccessManagementController.RenameAdminRoleAsync) or
            nameof(AdminAccessManagementController.SetAdminRoleActiveStateAsync) or
            nameof(AdminAccessManagementController.ReplaceAdminRolePermissionsAsync);
    }

    private static bool ReturnsEntityTag(string actionName)
    {
        return actionName is nameof(AdminAccessManagementController.CreateAdministratorAsync) or
            nameof(AdminAccessManagementController.GetAdministratorAsync) or
            nameof(AdminAccessManagementController.ChangeAdministratorDisplayNameAsync) or
            nameof(AdminAccessManagementController.ReplaceAdministratorRolesAsync) or
            nameof(AdminAccessManagementController.SetAdministratorActiveStateAsync) or
            nameof(AdminAccessManagementController.ResetAdministratorPasswordAsync) or
            nameof(AdminAccessManagementController.CreateAdminRoleAsync) or
            nameof(AdminAccessManagementController.GetAdminRoleAsync) or
            nameof(AdminAccessManagementController.RenameAdminRoleAsync) or
            nameof(AdminAccessManagementController.SetAdminRoleActiveStateAsync) or
            nameof(AdminAccessManagementController.ReplaceAdminRolePermissionsAsync);
    }
}
