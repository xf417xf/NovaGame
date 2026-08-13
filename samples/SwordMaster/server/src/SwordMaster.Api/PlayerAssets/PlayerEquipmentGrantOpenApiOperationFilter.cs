using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SwordMaster.Api.PlayerAssets;

public sealed class PlayerEquipmentGrantOpenApiOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.MethodInfo.DeclaringType != typeof(PlayerEquipmentGrantsController))
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
                        Id = "Bearer"
                    }
                }] = Array.Empty<string>()
            }
        ];

        if (context.MethodInfo.Name is
            nameof(PlayerEquipmentGrantsController.GrantAsync) or
            nameof(PlayerEquipmentGrantsController.RollbackAsync))
        {
            if (context.MethodInfo.Name ==
                nameof(PlayerEquipmentGrantsController.GrantAsync))
            {
                RequireRequestProperties(
                    operation,
                    context,
                    "configuredItemId",
                    "reason");
            }
            else
            {
                RequireRequestProperties(operation, context, "reason");
            }

            operation.Parameters ??= [];
            AddParameterIfMissing(
                operation,
                new OpenApiParameter
                {
                    Name = PlayerEquipmentGrantsController.IfMatchHeaderName,
                    In = ParameterLocation.Header,
                    Required = true,
                    Description = "Exactly one strong ETag containing the canonical positive decimal PlayerAssets revision, for example \"1\".",
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
                    Name = PlayerEquipmentGrantsController.IdempotencyKeyHeaderName,
                    In = ParameterLocation.Header,
                    Required = true,
                    Description = "Exactly one non-empty canonical lowercase D-format GUID for this retryable mutation.",
                    Schema = new OpenApiSchema
                    {
                        Type = "string",
                        Format = "uuid",
                        Pattern = "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
                    }
                });
        }

        if (operation.Responses.TryGetValue("200", out var successResponse))
        {
            successResponse.Headers ??=
                new Dictionary<string, OpenApiHeader>(StringComparer.OrdinalIgnoreCase);
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

    private static void RequireRequestProperties(
        OpenApiOperation operation,
        OperationFilterContext context,
        params string[] propertyNames)
    {
        var requestBody = operation.RequestBody
            ?? throw new InvalidOperationException(
                "An equipment-grant mutation is missing its request body.");
        requestBody.Required = true;

        foreach (var mediaType in requestBody.Content.Values)
        {
            var schema = ResolveSchema(mediaType.Schema, context.SchemaRepository);
            schema.Required = new HashSet<string>(
                propertyNames,
                StringComparer.Ordinal);
            foreach (var propertyName in propertyNames)
            {
                if (!schema.Properties.TryGetValue(
                        propertyName,
                        out var propertySchema))
                {
                    throw new InvalidOperationException(
                        $"The equipment-grant request schema is missing '{propertyName}'.");
                }

                propertySchema.Nullable = false;
            }
        }
    }

    private static OpenApiSchema ResolveSchema(
        OpenApiSchema schema,
        SchemaRepository repository)
    {
        if (schema.Reference is null)
        {
            return schema;
        }

        return repository.Schemas.TryGetValue(
            schema.Reference.Id,
            out var referencedSchema)
            ? referencedSchema
            : throw new InvalidOperationException(
                $"The equipment-grant request schema reference '{schema.Reference.Id}' is missing.");
    }

    private static void AddParameterIfMissing(
        OpenApiOperation operation,
        OpenApiParameter parameter)
    {
        if (operation.Parameters!.Any(existing =>
                existing.In == parameter.In &&
                string.Equals(
                    existing.Name,
                    parameter.Name,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        operation.Parameters.Add(parameter);
    }
}
