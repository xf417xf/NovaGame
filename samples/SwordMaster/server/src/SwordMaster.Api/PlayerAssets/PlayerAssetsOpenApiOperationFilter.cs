using Microsoft.AspNetCore.Authorization;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SwordMaster.Api.PlayerAssets;

public sealed class PlayerAssetsOpenApiOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var endpointMetadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;
        var requiresPlayerAuthentication = endpointMetadata
            .OfType<IAuthorizeData>()
            .SelectMany(authorizeData => (authorizeData.AuthenticationSchemes ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Any(scheme => string.Equals(
                scheme,
                SwordMasterPlayerAuthentication.Scheme,
                StringComparison.Ordinal));
        if (!requiresPlayerAuthentication)
        {
            return;
        }

        operation.Security =
        [
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = SwordMasterPlayerAuthentication.Scheme
                    }
                }] = Array.Empty<string>()
            }
        ];

        if (context.MethodInfo.DeclaringType != typeof(PlayerAssetsController))
        {
            return;
        }

        if (context.MethodInfo.Name is
            nameof(PlayerAssetsController.EquipAsync) or
            nameof(PlayerAssetsController.UnequipAsync))
        {
            operation.Parameters ??= [];
            AddParameterIfMissing(
                operation,
                new OpenApiParameter
                {
                    Name = PlayerAssetsController.IfMatchHeaderName,
                    In = ParameterLocation.Header,
                    Required = true,
                    Description = "Exactly one strong ETag containing the positive decimal PlayerAssets revision, for example \"1\". Weak tags, wildcard tags, and tag lists are rejected.",
                    Schema = new OpenApiSchema
                    {
                        Type = "string",
                        Example = new OpenApiString("\"1\"")
                    }
                });
            AddParameterIfMissing(
                operation,
                new OpenApiParameter
                {
                    Name = PlayerAssetsController.IdempotencyKeyHeaderName,
                    In = ParameterLocation.Header,
                    Required = true,
                    Description = "A non-empty canonical D-format GUID that identifies this retryable mutation.",
                    Schema = new OpenApiSchema
                    {
                        Type = "string",
                        Format = "uuid"
                    }
                });
        }

        if (operation.Responses.TryGetValue("200", out var successResponse))
        {
            successResponse.Headers ??= new Dictionary<string, OpenApiHeader>(StringComparer.OrdinalIgnoreCase);
            successResponse.Headers[HeaderNames.ETag] = new OpenApiHeader
            {
                Description = "Strong ETag containing the current positive decimal PlayerAssets revision.",
                Schema = new OpenApiSchema
                {
                    Type = "string",
                    Example = new OpenApiString("\"1\"")
                }
            };
        }
    }

    private static void AddParameterIfMissing(
        OpenApiOperation operation,
        OpenApiParameter parameter)
    {
        if (operation.Parameters!.Any(existing =>
                existing.In == parameter.In &&
                string.Equals(existing.Name, parameter.Name, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        operation.Parameters.Add(parameter);
    }
}
