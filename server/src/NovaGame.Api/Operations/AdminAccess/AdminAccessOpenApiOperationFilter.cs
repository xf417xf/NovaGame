using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace NovaGame.Api.Operations.AdminAccess;

public sealed class AdminAccessOpenApiOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var endpointMetadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;
        var isAnonymous = endpointMetadata.OfType<IAllowAnonymous>().Any();
        var requiresAuthorization = endpointMetadata.OfType<IAuthorizeData>().Any();

        if (!isAnonymous && requiresAuthorization)
        {
            operation.Security =
            [
                new OpenApiSecurityRequirement
                {
                    [new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    }] = Array.Empty<string>()
                }
            ];
        }

        if (context.MethodInfo.DeclaringType != typeof(AdminAccessController) ||
            context.MethodInfo.Name is not (
                nameof(AdminAccessController.RefreshAsync) or
                nameof(AdminAccessController.LogoutAsync)))
        {
            return;
        }

        operation.Parameters ??= [];
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = AdminAccessController.RefreshTokenCookieName,
            In = ParameterLocation.Cookie,
            Required = context.MethodInfo.Name == nameof(AdminAccessController.RefreshAsync),
            Description = "Opaque refresh token issued as a secure HttpOnly cookie.",
            Schema = new OpenApiSchema
            {
                Type = "string"
            }
        });
    }
}
