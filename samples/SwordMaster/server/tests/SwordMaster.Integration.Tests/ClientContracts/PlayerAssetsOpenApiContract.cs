using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Extensions;
using Microsoft.OpenApi.Models;
using SwordMaster.Api.PlayerAssets;

namespace SwordMaster.Integration.Tests.ClientContracts;

internal static class PlayerAssetsOpenApiContract
{
    internal const string AttributesPath =
        "/api/player-assets/characters/{characterId}/attributes";
    internal const string EquipmentPath =
        "/api/player-assets/characters/{characterId}/equipment/{slot}";

    private const string SchemaReferencePrefix = "#/components/schemas/";

    private static readonly HashSet<string> HttpMethods =
        new(StringComparer.Ordinal)
        {
            "delete",
            "get",
            "head",
            "options",
            "patch",
            "post",
            "put",
            "trace"
        };

    internal static string CreateCanonicalJson(OpenApiDocument source)
    {
        var sourceJson = source.SerializeAsJson(OpenApiSpecVersion.OpenApi3_0);
        var sourceRoot = JsonNode.Parse(sourceJson) as JsonObject
            ?? throw new InvalidOperationException("The generated OpenAPI document is not a JSON object.");
        var sourcePaths = RequireObject(sourceRoot, "paths");
        ValidateSourceSurface(sourcePaths);

        var filteredPaths = new JsonObject
        {
            [AttributesPath] = SelectOperations(
                RequireObject(sourcePaths, AttributesPath),
                "get"),
            [EquipmentPath] = SelectOperations(
                RequireObject(sourcePaths, EquipmentPath),
                "delete",
                "put")
        };
        ValidatePlayerSecurity(filteredPaths);

        var sourceComponents = RequireObject(sourceRoot, "components");
        var sourceSchemas = RequireObject(sourceComponents, "schemas");
        var selectedSchemas = SelectReferencedSchemas(filteredPaths, sourceSchemas);
        var sourceSecuritySchemes = RequireObject(sourceComponents, "securitySchemes");
        var playerSecurityScheme = sourceSecuritySchemes[
                SwordMasterPlayerAuthentication.Scheme]
            ?? throw new InvalidOperationException(
                $"The generated OpenAPI document is missing the '{SwordMasterPlayerAuthentication.Scheme}' security scheme.");

        var filteredRoot = new JsonObject
        {
            ["openapi"] = RequireNode(sourceRoot, "openapi").DeepClone(),
            ["info"] = RequireNode(sourceRoot, "info").DeepClone(),
            ["paths"] = filteredPaths,
            ["components"] = new JsonObject
            {
                ["schemas"] = selectedSchemas,
                ["securitySchemes"] = new JsonObject
                {
                    [SwordMasterPlayerAuthentication.Scheme] =
                        playerSecurityScheme.DeepClone()
                }
            }
        };

        return SerializeCanonical(filteredRoot);
    }

    private static JsonObject SelectOperations(
        JsonObject sourcePath,
        params string[] methods)
    {
        var selected = new JsonObject();
        foreach (var method in methods)
        {
            selected[method] = RequireNode(sourcePath, method).DeepClone();
        }

        return selected;
    }

    private static JsonObject SelectReferencedSchemas(
        JsonObject paths,
        JsonObject sourceSchemas)
    {
        var selectedNames = new HashSet<string>(StringComparer.Ordinal);
        var pendingNames = new Queue<string>();

        CollectSchemaReferences(paths, AddSchema);
        while (pendingNames.TryDequeue(out var schemaName))
        {
            var schema = sourceSchemas[schemaName]
                ?? throw new InvalidOperationException(
                    $"The generated OpenAPI document references missing schema '{schemaName}'.");
            CollectSchemaReferences(schema, AddSchema);
        }

        var selectedSchemas = new JsonObject();
        foreach (var schemaName in selectedNames.Order(StringComparer.Ordinal))
        {
            selectedSchemas[schemaName] = sourceSchemas[schemaName]!.DeepClone();
        }

        return selectedSchemas;

        void AddSchema(string schemaName)
        {
            if (selectedNames.Add(schemaName))
            {
                pendingNames.Enqueue(schemaName);
            }
        }
    }

    private static void CollectSchemaReferences(
        JsonNode? node,
        Action<string> addSchema)
    {
        switch (node)
        {
            case JsonObject jsonObject:
                foreach (var (name, value) in jsonObject)
                {
                    if (name == "$ref")
                    {
                        var reference = value?.GetValue<string>()
                            ?? throw new InvalidOperationException(
                                "The generated OpenAPI document contains a non-string $ref.");
                        if (!reference.StartsWith(
                                SchemaReferencePrefix,
                                StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException(
                                $"The focused PlayerAssets contract contains unsupported reference '{reference}'.");
                        }

                        addSchema(DecodeJsonPointerToken(
                            reference[SchemaReferencePrefix.Length..]));
                        continue;
                    }

                    CollectSchemaReferences(value, addSchema);
                }

                break;
            case JsonArray jsonArray:
                foreach (var item in jsonArray)
                {
                    CollectSchemaReferences(item, addSchema);
                }

                break;
        }
    }

    private static void ValidateSourceSurface(JsonObject paths)
    {
        var actualPaths = paths
            .Where(property => property.Key.StartsWith(
                "/api/player-assets/",
                StringComparison.Ordinal))
            .Select(property => property.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var expectedPaths = new[] { AttributesPath, EquipmentPath }
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (!actualPaths.SequenceEqual(expectedPaths, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"The real Program exposes an unapproved PlayerAssets path set: {string.Join(", ", actualPaths)}.");
        }

        ValidateOperations(RequireObject(paths, AttributesPath), "get");
        ValidateOperations(
            RequireObject(paths, EquipmentPath),
            "delete",
            "put");
    }

    private static void ValidateOperations(
        JsonObject path,
        params string[] expectedMethods)
    {
        var actualMethods = path
            .Where(property => HttpMethods.Contains(property.Key))
            .Select(property => property.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var expected = expectedMethods.Order(StringComparer.Ordinal).ToArray();
        if (!actualMethods.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"The real Program exposes an unapproved PlayerAssets operation set: {string.Join(", ", actualMethods)}.");
        }
    }

    private static void ValidatePlayerSecurity(JsonObject paths)
    {
        foreach (var path in paths.Select(property => RequireObject(paths, property.Key)))
        {
            foreach (var operation in path
                         .Where(property => HttpMethods.Contains(property.Key))
                         .Select(property => property.Value as JsonObject
                             ?? throw new InvalidOperationException(
                                 "The generated OpenAPI operation is not a JSON object.")))
            {
                var security = operation["security"] as JsonArray
                    ?? throw new InvalidOperationException(
                        "A PlayerAssets operation is missing its security requirement.");
                if (security.Count != 1 || security[0] is not JsonObject requirement ||
                    requirement.Count != 1 ||
                    !requirement.ContainsKey(SwordMasterPlayerAuthentication.Scheme))
                {
                    throw new InvalidOperationException(
                        $"Every PlayerAssets operation must require only '{SwordMasterPlayerAuthentication.Scheme}'.");
                }
            }
        }
    }

    private static string SerializeCanonical(JsonNode root)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(
                   stream,
                   new JsonWriterOptions { Indented = true }))
        {
            WriteCanonicalNode(writer, root);
        }

        return Encoding.UTF8
            .GetString(stream.ToArray())
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .TrimEnd('\n') + "\n";
    }

    private static void WriteCanonicalNode(Utf8JsonWriter writer, JsonNode? node)
    {
        switch (node)
        {
            case null:
                writer.WriteNullValue();
                break;
            case JsonObject jsonObject:
                writer.WriteStartObject();
                foreach (var property in jsonObject.OrderBy(
                             property => property.Key,
                             StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Key);
                    WriteCanonicalNode(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonArray jsonArray:
                writer.WriteStartArray();
                foreach (var item in jsonArray)
                {
                    WriteCanonicalNode(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                node.WriteTo(writer);
                break;
        }
    }

    private static JsonNode RequireNode(JsonObject owner, string propertyName)
    {
        return owner[propertyName]
            ?? throw new InvalidOperationException(
                $"The generated OpenAPI document is missing '{propertyName}'.");
    }

    private static JsonObject RequireObject(JsonObject owner, string propertyName)
    {
        return owner[propertyName] as JsonObject
            ?? throw new InvalidOperationException(
                $"The generated OpenAPI document is missing object '{propertyName}'.");
    }

    private static string DecodeJsonPointerToken(string value)
    {
        return value
            .Replace("~1", "/", StringComparison.Ordinal)
            .Replace("~0", "~", StringComparison.Ordinal);
    }
}
