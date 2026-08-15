using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.Swagger;
using Xunit;

namespace NovaGame.EntityFrameworkCore.Tests;

[Collection(MySqlDatabaseCollection.Name)]
public sealed class AdminAccessManagementOpenApiTests
{
    private const string SeedUserName = "admin";
    private const string SeedPassword = "Test-Administrator-Password-123!";

    private static readonly string[] ForbiddenResponseProperties =
    [
        "password",
        "initialPassword",
        "newPassword",
        "passwordHash",
        "refreshToken",
        "token",
        "tokenHash",
        "sessionId",
        "replacedBySessionId",
        "requestBody",
        "requestFingerprint",
        "idempotencyKey",
        "commandKind"
    ];

    private readonly MySqlDatabaseFixture _fixture;

    public AdminAccessManagementOpenApiTests(MySqlDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task OpenApiDocumentsExactManagementPathsSecurityHeadersAndStatusCodes()
    {
        await using var factory = CreateFactory();
        var swagger = factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        var specifications = GetOperationSpecifications();

        var managementPaths = swagger.Paths
            .Where(path => path.Key.StartsWith("/api/admin-access/", StringComparison.Ordinal))
            .ToDictionary(path => path.Key, path => path.Value, StringComparer.Ordinal);

        Assert.Equal(
            specifications.Select(specification => specification.Path).Distinct().OrderBy(path => path),
            managementPaths.Keys.OrderBy(path => path));
        Assert.Equal(17, specifications.Length);

        foreach (var pathGroup in specifications.GroupBy(specification => specification.Path))
        {
            var pathItem = managementPaths[pathGroup.Key];
            Assert.Equal(
                pathGroup.Select(specification => specification.Method).OrderBy(method => method),
                pathItem.Operations.Keys.OrderBy(method => method));
        }

        foreach (var specification in specifications)
        {
            var operation = managementPaths[specification.Path].Operations[specification.Method];

            AssertBearerSecurity(operation);
            AssertStatusCodes(operation, specification.StatusCodes);
            AssertQueryAndPathParameters(operation, specification);
            AssertRequestBodySchema(swagger, operation, specification.RequestSchemaId);
            AssertManagementHeaders(operation, specification);
            AssertSuccessEnvelope(swagger, operation, specification.ResponseDataSchemaId);
            AssertErrorEnvelopes(swagger, operation, specification.StatusCodes);
        }
    }

    [Fact]
    public async Task OpenApiDocumentsRedactedManagementResponseSchemas()
    {
        await using var factory = CreateFactory();
        var swagger = factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");

        AssertSchemaProperties(
            swagger,
            "AdministratorDto",
            "id",
            "userName",
            "displayName",
            "isActive",
            "roles",
            "createdAtUtc",
            "updatedAtUtc",
            "lastLoginAtUtc",
            "revision");
        AssertSchemaProperties(swagger, "AdminRoleReferenceDto", "id", "name", "isBuiltIn", "isActive");
        AssertSchemaProperties(
            swagger,
            "AdminRoleSummaryDto",
            "id",
            "name",
            "isBuiltIn",
            "isActive",
            "permissionCount",
            "administratorCount",
            "createdAtUtc",
            "updatedAtUtc",
            "revision");
        AssertSchemaProperties(
            swagger,
            "AdminRoleDto",
            "id",
            "name",
            "isBuiltIn",
            "isActive",
            "permissionCount",
            "administratorCount",
            "createdAtUtc",
            "updatedAtUtc",
            "revision",
            "permissionNames");
        AssertSchemaProperties(swagger, "PermissionGroupDto", "name", "displayName", "permissions");
        AssertSchemaProperties(swagger, "PermissionDefinitionDto", "name", "displayName", "description");
        AssertSchemaProperties(
            swagger,
            "AdminSessionFamilyDto",
            "familyId",
            "administratorId",
            "userName",
            "displayName",
            "firstCreatedAtUtc",
            "lastActivityAtUtc",
            "expiresAtUtc",
            "lastIpAddress",
            "state",
            "revokedAtUtc",
            "revocationReason");
        AssertSchemaProperties(
            swagger,
            "AdminAccessAuditRecordDto",
            "id",
            "action",
            "occurredAtUtc",
            "succeeded",
            "subjectAdministratorId",
            "actorAdministratorId",
            "roleId",
            "sessionFamilyId",
            "subjectUserName",
            "permissionName",
            "errorCode",
            "ipAddress",
            "userAgent",
            "correlationId",
            "targetRevision");

        AssertEnumSchema(swagger, "AdminSessionFamilyState");
        AssertEnumSchema(swagger, "AdminSessionRevocationReason");
        AssertEnumSchema(swagger, "AdminAccessAuditActionDto");

        foreach (var specification in GetOperationSpecifications())
        {
            var operation = swagger.Paths[specification.Path].Operations[specification.Method];
            var responseSchema = GetJsonResponseSchema(swagger, GetResponse(operation, "200"));
            var responsePropertyNames = GetReachablePropertyNames(swagger, responseSchema);

            Assert.All(
                responsePropertyNames,
                propertyName => Assert.DoesNotContain(
                    ForbiddenResponseProperties,
                    forbidden => string.Equals(forbidden, propertyName, StringComparison.OrdinalIgnoreCase)));
        }
    }

    [Fact]
    public async Task RealProgramManagementContractMatchesTrackedArtifact()
    {
        await using var factory = CreateFactory();
        var source = factory.Services
            .GetRequiredService<ISwaggerProvider>()
            .GetSwagger("v1");
        var actual = AdminAccessManagementOpenApiContract.CreateCanonicalJson(source);
        var capturePath = Environment.GetEnvironmentVariable(
            AdminAccessManagementOpenApiContract.CapturePathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(capturePath))
        {
            AdminAccessManagementOpenApiContract.WriteCapture(capturePath, actual);
            return;
        }

        var expectedPath = AdminAccessManagementOpenApiContract.GetTrackedContractPath();
        Assert.True(
            File.Exists(expectedPath),
            $"Tracked administrator-management OpenAPI contract was not found at '{expectedPath}'.");
        var expectedBytes = File.ReadAllBytes(expectedPath);

        Assert.False(expectedBytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        var expected = new UTF8Encoding(false, true).GetString(expectedBytes);
        Assert.Equal(
            actual,
            AdminAccessManagementOpenApiContract.CanonicalizeJson(expected));
    }

    private NovaGameApiFactory CreateFactory()
    {
        return new NovaGameApiFactory(
            _fixture.ConnectionString,
            SeedUserName,
            SeedPassword);
    }

    private static ManagementOperationSpecification[] GetOperationSpecifications()
    {
        return
        [
            new(
                "/api/admin-access/administrators",
                OperationType.Get,
                "AdministratorDto",
                ["200", "400", "401", "403", "500"],
                QueryParameters: ["Search", "IsActive", "RoleId", "Page", "PageSize"]),
            new(
                "/api/admin-access/administrators",
                OperationType.Post,
                "AdministratorDto",
                ["200", "400", "401", "403", "404", "409", "422", "500"],
                RequestSchemaId: "CreateAdministratorRequest",
                RequiresIdempotencyKey: true,
                ReturnsEntityTag: true),
            new(
                "/api/admin-access/administrators/{administratorId}",
                OperationType.Get,
                "AdministratorDto",
                ["200", "400", "401", "403", "404", "500"],
                PathParameters: ["administratorId"],
                ReturnsEntityTag: true),
            new(
                "/api/admin-access/administrators/{administratorId}/display-name",
                OperationType.Put,
                "AdministratorDto",
                ["200", "400", "401", "403", "404", "409", "422", "500"],
                RequestSchemaId: "ChangeAdministratorDisplayNameRequest",
                PathParameters: ["administratorId"],
                RequiresIfMatch: true,
                ReturnsEntityTag: true),
            new(
                "/api/admin-access/administrators/{administratorId}/roles",
                OperationType.Put,
                "AdministratorDto",
                ["200", "400", "401", "403", "404", "409", "422", "500"],
                RequestSchemaId: "ReplaceAdministratorRolesRequest",
                PathParameters: ["administratorId"],
                RequiresIfMatch: true,
                ReturnsEntityTag: true),
            new(
                "/api/admin-access/administrators/{administratorId}/active-state",
                OperationType.Put,
                "AdministratorDto",
                ["200", "400", "401", "403", "404", "409", "422", "500"],
                RequestSchemaId: "SetAdministratorActiveStateRequest",
                PathParameters: ["administratorId"],
                RequiresIfMatch: true,
                ReturnsEntityTag: true),
            new(
                "/api/admin-access/administrators/{administratorId}/password-reset",
                OperationType.Post,
                "AdministratorDto",
                ["200", "400", "401", "403", "404", "409", "422", "500"],
                RequestSchemaId: "ResetAdministratorPasswordRequest",
                PathParameters: ["administratorId"],
                RequiresIdempotencyKey: true,
                RequiresIfMatch: true,
                ReturnsEntityTag: true),
            new(
                "/api/admin-access/roles",
                OperationType.Get,
                "AdminRoleSummaryDto",
                ["200", "400", "401", "403", "500"],
                QueryParameters: ["Search", "IsActive", "IsBuiltIn", "Page", "PageSize"]),
            new(
                "/api/admin-access/roles",
                OperationType.Post,
                "AdminRoleDto",
                ["200", "400", "401", "403", "409", "422", "500"],
                RequestSchemaId: "CreateAdminRoleRequest",
                RequiresIdempotencyKey: true,
                ReturnsEntityTag: true),
            new(
                "/api/admin-access/roles/{roleId}",
                OperationType.Get,
                "AdminRoleDto",
                ["200", "400", "401", "403", "404", "500"],
                PathParameters: ["roleId"],
                ReturnsEntityTag: true),
            new(
                "/api/admin-access/roles/{roleId}/name",
                OperationType.Put,
                "AdminRoleDto",
                ["200", "400", "401", "403", "404", "409", "422", "500"],
                RequestSchemaId: "RenameAdminRoleRequest",
                PathParameters: ["roleId"],
                RequiresIfMatch: true,
                ReturnsEntityTag: true),
            new(
                "/api/admin-access/roles/{roleId}/active-state",
                OperationType.Put,
                "AdminRoleDto",
                ["200", "400", "401", "403", "404", "409", "422", "500"],
                RequestSchemaId: "SetAdminRoleActiveStateRequest",
                PathParameters: ["roleId"],
                RequiresIfMatch: true,
                ReturnsEntityTag: true),
            new(
                "/api/admin-access/roles/{roleId}/permissions",
                OperationType.Put,
                "AdminRoleDto",
                ["200", "400", "401", "403", "404", "409", "422", "500"],
                RequestSchemaId: "ReplaceAdminRolePermissionsRequest",
                PathParameters: ["roleId"],
                RequiresIfMatch: true,
                ReturnsEntityTag: true),
            new(
                "/api/admin-access/permissions",
                OperationType.Get,
                "PermissionGroupDto",
                ["200", "401", "403", "500"]),
            new(
                "/api/admin-access/sessions",
                OperationType.Get,
                "AdminSessionFamilyDto",
                ["200", "400", "401", "403", "500"],
                QueryParameters: ["AdministratorId", "State", "Page", "PageSize"]),
            new(
                "/api/admin-access/sessions/{familyId}/revoke",
                OperationType.Post,
                "AdminSessionFamilyDto",
                ["200", "400", "401", "403", "404", "500"],
                PathParameters: ["familyId"]),
            new(
                "/api/admin-access/audit-records",
                OperationType.Get,
                "AdminAccessAuditRecordDto",
                ["200", "400", "401", "403", "500"],
                QueryParameters:
                [
                    "FromUtc",
                    "ToUtc",
                    "Action",
                    "Succeeded",
                    "ActorAdministratorId",
                    "SubjectAdministratorId",
                    "RoleId",
                    "CorrelationId",
                    "Page",
                    "PageSize"
                ])
        ];
    }

    private static void AssertBearerSecurity(OpenApiOperation operation)
    {
        var requirement = Assert.Single(operation.Security);
        var scheme = Assert.Single(requirement.Keys);

        Assert.Equal("Bearer", scheme.Reference.Id);
        Assert.Empty(requirement[scheme]);
    }

    private static void AssertStatusCodes(OpenApiOperation operation, IReadOnlyCollection<string> expectedStatusCodes)
    {
        Assert.Equal(
            expectedStatusCodes.OrderBy(statusCode => statusCode, StringComparer.Ordinal),
            operation.Responses.Keys.OrderBy(statusCode => statusCode, StringComparer.Ordinal));
    }

    private static void AssertQueryAndPathParameters(
        OpenApiOperation operation,
        ManagementOperationSpecification specification)
    {
        var parameters = operation.Parameters ?? [];
        Assert.Equal(
            specification.QueryParameterNames.OrderBy(name => name, StringComparer.Ordinal),
            parameters
                .Where(parameter => parameter.In == ParameterLocation.Query)
                .Select(parameter => parameter.Name)
                .OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(
            specification.PathParameterNames.OrderBy(name => name, StringComparer.Ordinal),
            parameters
                .Where(parameter => parameter.In == ParameterLocation.Path)
                .Select(parameter => parameter.Name)
                .OrderBy(name => name, StringComparer.Ordinal));
    }

    private static void AssertRequestBodySchema(
        OpenApiDocument swagger,
        OpenApiOperation operation,
        string? expectedSchemaId)
    {
        if (expectedSchemaId is null)
        {
            Assert.Null(operation.RequestBody);
            return;
        }

        var requestBody = Assert.IsType<OpenApiRequestBody>(operation.RequestBody);
        Assert.True(requestBody.Required);
        Assert.Equal(
            GetExpectedRequestPropertyNames(expectedSchemaId)
                .OrderBy(name => name, StringComparer.Ordinal),
            GetTopLevelSchemaPropertyNames(swagger, GetRawJsonSchema(requestBody.Content))
                .OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(
            GetExpectedRequestPropertyNames(expectedSchemaId)
                .OrderBy(name => name, StringComparer.Ordinal),
            GetTopLevelRequiredSchemaPropertyNames(swagger, GetRawJsonSchema(requestBody.Content))
                .OrderBy(name => name, StringComparer.Ordinal));
    }

    private static void AssertManagementHeaders(
        OpenApiOperation operation,
        ManagementOperationSpecification specification)
    {
        var expectedHeaderNames = new List<string>();
        if (specification.RequiresIdempotencyKey)
        {
            expectedHeaderNames.Add("Idempotency-Key");
        }

        if (specification.RequiresIfMatch)
        {
            expectedHeaderNames.Add("If-Match");
        }

        var headers = (operation.Parameters ?? [])
            .Where(parameter => parameter.In == ParameterLocation.Header)
            .ToArray();
        Assert.Equal(
            expectedHeaderNames.OrderBy(name => name, StringComparer.Ordinal),
            headers.Select(header => header.Name).OrderBy(name => name, StringComparer.Ordinal));

        foreach (var header in headers)
        {
            Assert.True(header.Required);
            Assert.Equal("string", header.Schema.Type);
        }

        if (specification.RequiresIdempotencyKey)
        {
            var idempotencyKey = Assert.Single(
                headers,
                header => string.Equals(header.Name, "Idempotency-Key", StringComparison.Ordinal));
            Assert.Contains("Canonical hyphenated GUID", idempotencyKey.Description, StringComparison.Ordinal);
        }

        if (specification.RequiresIfMatch)
        {
            var ifMatch = Assert.Single(
                headers,
                header => string.Equals(header.Name, "If-Match", StringComparison.Ordinal));
            Assert.Contains("Strong ETag", ifMatch.Description, StringComparison.Ordinal);
        }

        var successResponse = GetResponse(operation, "200");
        var entityTag = FindResponseHeader(successResponse, "ETag");
        if (specification.ReturnsEntityTag)
        {
            var header = Assert.IsType<OpenApiHeader>(entityTag);
            Assert.Equal("string", header.Schema.Type);
            Assert.Contains("Strong ETag", header.Description, StringComparison.Ordinal);
        }
        else
        {
            Assert.Null(entityTag);
        }

        var unauthorizedResponse = GetResponse(operation, "401");
        var authenticate = Assert.IsType<OpenApiHeader>(
            FindResponseHeader(unauthorizedResponse, "WWW-Authenticate"));
        Assert.Equal("string", authenticate.Schema.Type);
        Assert.Contains("Bearer", authenticate.Description, StringComparison.Ordinal);
    }

    private static void AssertSuccessEnvelope(
        OpenApiDocument swagger,
        OpenApiOperation operation,
        string expectedDataSchemaId)
    {
        var schema = GetJsonResponseSchema(swagger, GetResponse(operation, "200"));
        var envelope = ResolveSchema(swagger, schema);

        Assert.Equal(
            new[] { "data", "error", "succeeded" },
            envelope.Properties.Keys.OrderBy(name => name, StringComparer.Ordinal));
        Assert.True(
            ContainsSchemaReference(swagger, envelope.Properties["data"], expectedDataSchemaId),
            $"The success envelope must include {expectedDataSchemaId} as its data schema.");
    }

    private static void AssertErrorEnvelopes(
        OpenApiDocument swagger,
        OpenApiOperation operation,
        IEnumerable<string> statusCodes)
    {
        foreach (var statusCode in statusCodes.Where(statusCode => statusCode != "200"))
        {
            var schema = GetJsonResponseSchema(swagger, GetResponse(operation, statusCode));
            var envelope = ResolveSchema(swagger, schema);

            Assert.Contains("succeeded", envelope.Properties.Keys);
            Assert.Contains("error", envelope.Properties.Keys);
            Assert.DoesNotContain("data", envelope.Properties.Keys);
        }
    }

    private static OpenApiResponse GetResponse(OpenApiOperation operation, string statusCode)
    {
        Assert.True(operation.Responses.TryGetValue(statusCode, out var response));
        return Assert.IsType<OpenApiResponse>(response);
    }

    private static OpenApiHeader? FindResponseHeader(OpenApiResponse response, string name)
    {
        return response.Headers?
            .SingleOrDefault(header => string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase))
            .Value;
    }

    private static OpenApiSchema GetJsonResponseSchema(OpenApiDocument swagger, OpenApiResponse response)
    {
        return GetJsonSchema(swagger, response.Content);
    }

    private static OpenApiSchema GetRawJsonSchema(IDictionary<string, OpenApiMediaType> content)
    {
        Assert.True(content.TryGetValue("application/json", out var mediaType));
        return Assert.IsType<OpenApiSchema>(mediaType.Schema);
    }

    private static OpenApiSchema GetJsonSchema(
        OpenApiDocument swagger,
        IDictionary<string, OpenApiMediaType> content)
    {
        return ResolveSchema(swagger, GetRawJsonSchema(content));
    }

    private static IReadOnlyCollection<string> GetExpectedRequestPropertyNames(string schemaId)
    {
        return schemaId switch
        {
            "CreateAdministratorRequest" => ["userName", "displayName", "initialPassword", "roleIds"],
            "ChangeAdministratorDisplayNameRequest" => ["displayName"],
            "ReplaceAdministratorRolesRequest" => ["roleIds"],
            "SetAdministratorActiveStateRequest" => ["isActive"],
            "ResetAdministratorPasswordRequest" => ["newPassword"],
            "CreateAdminRoleRequest" => ["name", "permissionNames"],
            "RenameAdminRoleRequest" => ["name"],
            "SetAdminRoleActiveStateRequest" => ["isActive"],
            "ReplaceAdminRolePermissionsRequest" => ["permissionNames"],
            _ => throw new ArgumentOutOfRangeException(nameof(schemaId), schemaId, "Unknown request schema.")
        };
    }

    private static IReadOnlyCollection<string> GetTopLevelSchemaPropertyNames(
        OpenApiDocument swagger,
        OpenApiSchema schema,
        ISet<string>? visitedReferences = null)
    {
        var referenceId = schema.Reference?.Id;
        if (referenceId is not null)
        {
            visitedReferences ??= new HashSet<string>(StringComparer.Ordinal);
            if (!visitedReferences.Add(referenceId) ||
                !swagger.Components.Schemas.TryGetValue(referenceId, out var referencedSchema))
            {
                return [];
            }

            return GetTopLevelSchemaPropertyNames(swagger, referencedSchema, visitedReferences);
        }

        var properties = schema.Properties.Keys.ToHashSet(StringComparer.Ordinal);
        foreach (var candidate in schema.AllOf.Concat(schema.AnyOf).Concat(schema.OneOf))
        {
            properties.UnionWith(GetTopLevelSchemaPropertyNames(swagger, candidate, visitedReferences));
        }

        return properties;
    }

    private static IReadOnlyCollection<string> GetTopLevelRequiredSchemaPropertyNames(
        OpenApiDocument swagger,
        OpenApiSchema schema,
        ISet<string>? visitedReferences = null)
    {
        var referenceId = schema.Reference?.Id;
        if (referenceId is not null)
        {
            visitedReferences ??= new HashSet<string>(StringComparer.Ordinal);
            if (!visitedReferences.Add(referenceId) ||
                !swagger.Components.Schemas.TryGetValue(referenceId, out var referencedSchema))
            {
                return [];
            }

            return GetTopLevelRequiredSchemaPropertyNames(swagger, referencedSchema, visitedReferences);
        }

        var required = schema.Required.ToHashSet(StringComparer.Ordinal);
        foreach (var candidate in schema.AllOf.Concat(schema.AnyOf).Concat(schema.OneOf))
        {
            required.UnionWith(GetTopLevelRequiredSchemaPropertyNames(swagger, candidate, visitedReferences));
        }

        return required;
    }

    private static OpenApiSchema ResolveSchema(OpenApiDocument swagger, OpenApiSchema schema)
    {
        var referenceId = schema.Reference?.Id;
        if (referenceId is null)
        {
            return schema;
        }

        Assert.True(swagger.Components.Schemas.TryGetValue(referenceId, out var referencedSchema));
        return Assert.IsType<OpenApiSchema>(referencedSchema);
    }

    private static bool ContainsSchemaReference(
        OpenApiDocument swagger,
        OpenApiSchema schema,
        string expectedSchemaId,
        ISet<string>? visitedReferences = null)
    {
        var referenceId = schema.Reference?.Id;
        if (string.Equals(referenceId, expectedSchemaId, StringComparison.Ordinal))
        {
            return true;
        }

        visitedReferences ??= new HashSet<string>(StringComparer.Ordinal);
        if (referenceId is not null)
        {
            if (!visitedReferences.Add(referenceId) ||
                !swagger.Components.Schemas.TryGetValue(referenceId, out var referencedSchema))
            {
                return false;
            }

            return ContainsSchemaReference(swagger, referencedSchema, expectedSchemaId, visitedReferences);
        }

        if (schema.Items is not null &&
            ContainsSchemaReference(swagger, schema.Items, expectedSchemaId, visitedReferences))
        {
            return true;
        }

        if (schema.AllOf.Any(candidate =>
                ContainsSchemaReference(swagger, candidate, expectedSchemaId, visitedReferences)) ||
            schema.AnyOf.Any(candidate =>
                ContainsSchemaReference(swagger, candidate, expectedSchemaId, visitedReferences)) ||
            schema.OneOf.Any(candidate =>
                ContainsSchemaReference(swagger, candidate, expectedSchemaId, visitedReferences)))
        {
            return true;
        }

        return schema.Properties.Values.Any(property =>
            ContainsSchemaReference(swagger, property, expectedSchemaId, visitedReferences));
    }

    private static void AssertSchemaProperties(
        OpenApiDocument swagger,
        string schemaId,
        params string[] expectedPropertyNames)
    {
        Assert.True(swagger.Components.Schemas.TryGetValue(schemaId, out var schema));
        Assert.Equal(
            expectedPropertyNames.OrderBy(name => name, StringComparer.Ordinal),
            schema.Properties.Keys.OrderBy(name => name, StringComparer.Ordinal));
    }

    private static void AssertEnumSchema(OpenApiDocument swagger, string schemaId)
    {
        Assert.True(swagger.Components.Schemas.TryGetValue(schemaId, out var schema));
        Assert.NotEmpty(schema.Enum);
    }

    private static IReadOnlyList<string> GetReachablePropertyNames(
        OpenApiDocument swagger,
        OpenApiSchema schema)
    {
        var propertyNames = new List<string>();
        CollectReachablePropertyNames(
            swagger,
            schema,
            propertyNames,
            new HashSet<string>(StringComparer.Ordinal));
        return propertyNames;
    }

    private static void CollectReachablePropertyNames(
        OpenApiDocument swagger,
        OpenApiSchema schema,
        ICollection<string> propertyNames,
        ISet<string> visitedReferences)
    {
        var referenceId = schema.Reference?.Id;
        if (referenceId is not null)
        {
            if (!visitedReferences.Add(referenceId) ||
                !swagger.Components.Schemas.TryGetValue(referenceId, out var referencedSchema))
            {
                return;
            }

            CollectReachablePropertyNames(swagger, referencedSchema, propertyNames, visitedReferences);
            return;
        }

        foreach (var property in schema.Properties)
        {
            propertyNames.Add(property.Key);
            CollectReachablePropertyNames(swagger, property.Value, propertyNames, visitedReferences);
        }

        if (schema.Items is not null)
        {
            CollectReachablePropertyNames(swagger, schema.Items, propertyNames, visitedReferences);
        }

        foreach (var candidate in schema.AllOf.Concat(schema.AnyOf).Concat(schema.OneOf))
        {
            CollectReachablePropertyNames(swagger, candidate, propertyNames, visitedReferences);
        }
    }

    private sealed record ManagementOperationSpecification(
        string Path,
        OperationType Method,
        string ResponseDataSchemaId,
        IReadOnlyCollection<string> StatusCodes,
        string? RequestSchemaId = null,
        IReadOnlyCollection<string>? QueryParameters = null,
        IReadOnlyCollection<string>? PathParameters = null,
        bool RequiresIdempotencyKey = false,
        bool RequiresIfMatch = false,
        bool ReturnsEntityTag = false)
    {
        public IReadOnlyCollection<string> QueryParameterNames =>
            QueryParameters ?? Array.Empty<string>();

        public IReadOnlyCollection<string> PathParameterNames =>
            PathParameters ?? Array.Empty<string>();
    }
}
