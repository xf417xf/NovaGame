using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Extensions;
using Microsoft.OpenApi.Models;

namespace NovaGame.EntityFrameworkCore.Tests;

internal static class AdminAccessManagementOpenApiContract
{
    internal const string CapturePathEnvironmentVariable =
        "NOVAGAME_ADMIN_ACCESS_OPENAPI_CAPTURE_PATH";

    private const string AdministratorSecurityScheme = "Bearer";
    private const string AdministratorPathPrefix = "/api/admin-access";
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

    private static readonly OperationGroup[] ExpectedOperationGroups =
    [
        new("/api/admin-access/administrators", ["get", "post"]),
        new("/api/admin-access/administrators/{administratorId}", ["get"]),
        new("/api/admin-access/administrators/{administratorId}/display-name", ["put"]),
        new("/api/admin-access/administrators/{administratorId}/roles", ["put"]),
        new("/api/admin-access/administrators/{administratorId}/active-state", ["put"]),
        new("/api/admin-access/administrators/{administratorId}/password-reset", ["post"]),
        new("/api/admin-access/roles", ["get", "post"]),
        new("/api/admin-access/roles/{roleId}", ["get"]),
        new("/api/admin-access/roles/{roleId}/name", ["put"]),
        new("/api/admin-access/roles/{roleId}/active-state", ["put"]),
        new("/api/admin-access/roles/{roleId}/permissions", ["put"]),
        new("/api/admin-access/permissions", ["get"]),
        new("/api/admin-access/sessions", ["get"]),
        new("/api/admin-access/sessions/{familyId}/revoke", ["post"]),
        new("/api/admin-access/audit-records", ["get"])
    ];

    internal static string CreateCanonicalJson(OpenApiDocument source)
    {
        var sourceJson = source.SerializeAsJson(OpenApiSpecVersion.OpenApi3_0);
        var sourceRoot = JsonNode.Parse(sourceJson) as JsonObject
            ?? throw new InvalidOperationException(
                "The generated OpenAPI document is not a JSON object.");
        var sourcePaths = RequireObject(sourceRoot, "paths");
        ValidateSourceSurface(sourcePaths);

        var filteredPaths = new JsonObject();
        foreach (var group in ExpectedOperationGroups)
        {
            filteredPaths[group.Path] = SelectOperations(
                RequireObject(sourcePaths, group.Path),
                group.Methods);
        }

        ValidateAdministratorSecurity(filteredPaths);

        var sourceComponents = RequireObject(sourceRoot, "components");
        var sourceSchemas = RequireObject(sourceComponents, "schemas");
        var selectedSchemas = SelectReferencedSchemas(filteredPaths, sourceSchemas);
        var sourceSecuritySchemes = RequireObject(sourceComponents, "securitySchemes");
        var bearerSecurityScheme = sourceSecuritySchemes[AdministratorSecurityScheme]
            ?? throw new InvalidOperationException(
                $"The generated OpenAPI document is missing the '{AdministratorSecurityScheme}' security scheme.");

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
                    [AdministratorSecurityScheme] = bearerSecurityScheme.DeepClone()
                }
            }
        };

        return SerializeCanonical(filteredRoot);
    }

    internal static string CanonicalizeJson(string source)
    {
        var root = JsonNode.Parse(source) as JsonObject
            ?? throw new InvalidOperationException(
                "The tracked OpenAPI document is not a JSON object.");

        return SerializeCanonical(root);
    }

    internal static string GetTrackedContractPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NovaGame.sln")) &&
                Directory.Exists(Path.Combine(directory.FullName, "admin")) &&
                Directory.Exists(Path.Combine(directory.FullName, "server")))
            {
                return Path.Combine(
                    directory.FullName,
                    "admin",
                    "apps",
                    "web-ele",
                    "contracts",
                    "admin-access.openapi.json");
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Unable to locate the NovaGame repository root for the tracked management OpenAPI contract.");
    }

    internal static void WriteCapture(string requestedPath, string contract)
    {
        var outputPath = Path.GetFullPath(requestedPath);
        var temporaryRoot = Path.GetFullPath(Path.GetTempPath())
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!outputPath.StartsWith(
                temporaryRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The OpenAPI capture path must remain under '{temporaryRoot}'.");
        }

        var outputDirectory = Path.GetDirectoryName(outputPath)
            ?? throw new InvalidOperationException(
                "The OpenAPI capture path has no parent directory.");
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(
            outputPath,
            contract,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static JsonObject SelectOperations(
        JsonObject sourcePath,
        IReadOnlyCollection<string> methods)
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
                                $"The focused management contract contains unsupported reference '{reference}'.");
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
            .Where(property => IsManagementPath(property.Key))
            .Select(property => property.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var expectedPaths = ExpectedOperationGroups
            .Select(group => group.Path)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (!actualPaths.SequenceEqual(expectedPaths, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"The real Program exposes an unapproved management path set: {string.Join(", ", actualPaths)}.");
        }

        foreach (var group in ExpectedOperationGroups)
        {
            ValidateOperations(RequireObject(paths, group.Path), group.Methods);
        }
    }

    private static void ValidateOperations(
        JsonObject path,
        IReadOnlyCollection<string> expectedMethods)
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
                $"The real Program exposes an unapproved management operation set: {string.Join(", ", actualMethods)}.");
        }
    }

    private static bool IsManagementPath(string path)
    {
        return string.Equals(
                   path,
                   AdministratorPathPrefix,
                   StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith(
                   AdministratorPathPrefix + "/",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateAdministratorSecurity(JsonObject paths)
    {
        foreach (var path in paths.Select(
                     property => RequireObject(paths, property.Key)))
        {
            foreach (var operation in path
                         .Where(property => HttpMethods.Contains(property.Key))
                         .Select(property => property.Value as JsonObject
                             ?? throw new InvalidOperationException(
                                 "The generated OpenAPI operation is not a JSON object.")))
            {
                var security = operation["security"] as JsonArray
                    ?? throw new InvalidOperationException(
                        "A management operation is missing its security requirement.");
                if (security.Count != 1 ||
                    security[0] is not JsonObject requirement ||
                    requirement.Count != 1 ||
                    !requirement.ContainsKey(AdministratorSecurityScheme))
                {
                    throw new InvalidOperationException(
                        $"Every management operation must require only '{AdministratorSecurityScheme}'.");
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

    private static JsonObject RequireObject(
        JsonObject owner,
        string propertyName)
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

    private sealed record OperationGroup(
        string Path,
        IReadOnlyCollection<string> Methods);
}
