using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using SwordMaster.Api.PlayerAssets;
using SwordMaster.Integration.Tests.RuntimeComposition;
using Swashbuckle.AspNetCore.Swagger;
using Xunit;

namespace SwordMaster.Integration.Tests.ClientContracts;

[Collection(SwordMasterRuntimeCompositionCollection.Name)]
public sealed class PlayerEquipmentGrantOpenApiContractTests
{
    public const string CapturePathEnvironmentVariable =
        "SWORDMASTER_PLAYER_EQUIPMENT_GRANT_OPENAPI_CAPTURE_PATH";

    private const string EquipmentGrantsPath =
        PlayerEquipmentGrantOpenApiContract.EquipmentGrantsPath;
    private const string RollbackPath =
        PlayerEquipmentGrantOpenApiContract.RollbackPath;
    private const string IfMatchHeaderName = "If-Match";
    private const string IdempotencyKeyHeaderName = "Idempotency-Key";

    [Fact]
    public void RealProgramEquipmentGrantContractMatchesTrackedArtifact()
    {
        using var factory = new SwordMasterRuntimeApiFactory();
        var source = factory.Services
            .GetRequiredService<ISwaggerProvider>()
            .GetSwagger("v1");
        var actual = PlayerEquipmentGrantOpenApiContract.CreateCanonicalJson(
            source);

        AssertContractShape(actual);

        var capturePath = Environment.GetEnvironmentVariable(
            CapturePathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(capturePath))
        {
            WriteCapture(capturePath, actual);
            return;
        }

        var expectedPath = Path.Combine(
            AppContext.BaseDirectory,
            "ClientContracts",
            "player-equipment-grants.openapi.json");
        Assert.True(
            File.Exists(expectedPath),
            $"Tracked equipment-grant OpenAPI contract was not copied to '{expectedPath}'.");
        var expectedBytes = File.ReadAllBytes(expectedPath);
        var actualBytes = new UTF8Encoding(false).GetBytes(actual);

        Assert.False(expectedBytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        Assert.Equal(expectedBytes, actualBytes);
    }

    [Fact]
    public void RealProgramDocumentsOnlyTheApprovedAdministratorOperationsAndSecurity()
    {
        using var factory = new SwordMasterRuntimeApiFactory();
        var swagger = factory.Services
            .GetRequiredService<ISwaggerProvider>()
            .GetSwagger("v1");

        Assert.Contains("Bearer", swagger.Components.SecuritySchemes.Keys);
        Assert.Contains(
            SwordMasterPlayerAuthentication.Scheme,
            swagger.Components.SecuritySchemes.Keys);

        var operationPaths = swagger.Paths.Keys
            .Where(path => path.StartsWith(
                "/api/swordmaster/operations/player-assets/",
                StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            new[] { EquipmentGrantsPath, RollbackPath }
                .Order(StringComparer.Ordinal),
            operationPaths);

        var equipmentGrants = swagger.Paths[EquipmentGrantsPath];
        Assert.Equal(
            new[] { OperationType.Get, OperationType.Post },
            equipmentGrants.Operations.Keys.Order());
        var rollback = swagger.Paths[RollbackPath];
        Assert.Equal(
            [OperationType.Post],
            rollback.Operations.Keys);

        var get = equipmentGrants.Operations[OperationType.Get];
        var grant = equipmentGrants.Operations[OperationType.Post];
        var rollbackGrant = rollback.Operations[OperationType.Post];
        AssertBearerOnly(get);
        AssertBearerOnly(grant);
        AssertBearerOnly(rollbackGrant);

        AssertMutationHeaders(grant);
        AssertMutationHeaders(rollbackGrant);
        AssertNoMutationHeaders(get);
        AssertStrongEtagResponse(get);
        AssertStrongEtagResponse(grant);
        AssertStrongEtagResponse(rollbackGrant);

        AssertRequiredRequestProperties(
            swagger,
            grant,
            "configuredItemId",
            "reason");
        AssertRequiredRequestProperties(swagger, rollbackGrant, "reason");

        AssertResponses(get, "200", "400", "401", "403", "404", "500");
        AssertResponses(
            grant,
            "200",
            "400",
            "401",
            "403",
            "404",
            "409",
            "422",
            "500");
        AssertResponses(
            rollbackGrant,
            "200",
            "400",
            "401",
            "403",
            "404",
            "409",
            "500");
        Assert.DoesNotContain("422", rollbackGrant.Responses.Keys);
    }

    [Fact]
    public void AdministratorOperationsDoNotChangeExistingPlayerOrFrameworkSecurity()
    {
        using var factory = new SwordMasterRuntimeApiFactory();
        var swagger = factory.Services
            .GetRequiredService<ISwaggerProvider>()
            .GetSwagger("v1");

        AssertBearerOnly(
            swagger.Paths["/api/auth/codes"].Operations[OperationType.Get]);
        AssertBearerOnly(
            swagger.Paths["/api/game-config/drafts"].Operations[OperationType.Get]);
        AssertSecurityScheme(
            swagger.Paths[
                    "/api/player-assets/characters/{characterId}/equipment/{slot}"]
                .Operations[OperationType.Put],
            SwordMasterPlayerAuthentication.Scheme);
        AssertSecurityScheme(
            swagger.Paths[
                    "/api/player-assets/characters/{characterId}/equipment/{slot}"]
                .Operations[OperationType.Delete],
            SwordMasterPlayerAuthentication.Scheme);
        AssertSecurityScheme(
            swagger.Paths[
                    "/api/player-assets/characters/{characterId}/attributes"]
                .Operations[OperationType.Get],
            SwordMasterPlayerAuthentication.Scheme);
    }

    private static void AssertBearerOnly(OpenApiOperation operation)
    {
        AssertSecurityScheme(operation, "Bearer");
        Assert.DoesNotContain(
            operation.Security,
            requirement => requirement.Keys.Any(scheme => string.Equals(
                scheme.Reference.Id,
                SwordMasterPlayerAuthentication.Scheme,
                StringComparison.Ordinal)));
    }

    private static void AssertSecurityScheme(
        OpenApiOperation operation,
        string expectedScheme)
    {
        var requirement = Assert.Single(operation.Security);
        var scheme = Assert.Single(requirement.Keys);
        Assert.Equal(expectedScheme, scheme.Reference.Id);
    }

    private static void AssertMutationHeaders(OpenApiOperation operation)
    {
        var ifMatch = AssertRequiredHeader(operation, IfMatchHeaderName);
        Assert.Equal("string", ifMatch.Schema.Type);

        var idempotencyKey = AssertRequiredHeader(
            operation,
            IdempotencyKeyHeaderName);
        Assert.Equal("string", idempotencyKey.Schema.Type);
        Assert.Equal("uuid", idempotencyKey.Schema.Format);
    }

    private static OpenApiParameter AssertRequiredHeader(
        OpenApiOperation operation,
        string headerName)
    {
        var header = Assert.Single(
            operation.Parameters,
            parameter => parameter.In == ParameterLocation.Header &&
                string.Equals(
                    parameter.Name,
                    headerName,
                    StringComparison.OrdinalIgnoreCase));
        Assert.True(header.Required);
        return header;
    }

    private static void AssertNoMutationHeaders(OpenApiOperation operation)
    {
        Assert.DoesNotContain(
            operation.Parameters,
            parameter => parameter.In == ParameterLocation.Header &&
                (string.Equals(
                        parameter.Name,
                        IfMatchHeaderName,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        parameter.Name,
                        IdempotencyKeyHeaderName,
                        StringComparison.OrdinalIgnoreCase)));
    }

    private static void AssertStrongEtagResponse(OpenApiOperation operation)
    {
        var response = operation.Responses["200"];
        var etag = Assert.Single(
            response.Headers,
            header => string.Equals(
                header.Key,
                "ETag",
                StringComparison.OrdinalIgnoreCase));
        Assert.Equal("string", etag.Value.Schema.Type);
        Assert.False(string.IsNullOrWhiteSpace(etag.Value.Description));
    }

    private static void AssertRequiredRequestProperties(
        OpenApiDocument document,
        OpenApiOperation operation,
        params string[] expectedProperties)
    {
        Assert.NotNull(operation.RequestBody);
        Assert.True(operation.RequestBody.Required);
        var requestSchema = operation.RequestBody.Content["application/json"].Schema;
        var schema = requestSchema.Reference is null
            ? requestSchema
            : document.Components.Schemas[requestSchema.Reference.Id];
        Assert.Equal(
            expectedProperties.Order(StringComparer.Ordinal),
            schema.Properties.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(
            expectedProperties.Order(StringComparer.Ordinal),
            schema.Required.Order(StringComparer.Ordinal));
        Assert.All(
            expectedProperties,
            propertyName => Assert.False(schema.Properties[propertyName].Nullable));
    }

    private static void AssertResponses(
        OpenApiOperation operation,
        params string[] statusCodes)
    {
        Assert.Equal(
            statusCodes.Order(StringComparer.Ordinal),
            operation.Responses.Keys.Order(StringComparer.Ordinal));
        Assert.All(
            statusCodes,
            statusCode => Assert.Contains(
                "application/json",
                operation.Responses[statusCode].Content.Keys));
    }

    private static void AssertContractShape(string contract)
    {
        var root = JsonNode.Parse(contract) as JsonObject;
        Assert.NotNull(root);

        var paths = Assert.IsType<JsonObject>(root["paths"]);
        Assert.Equal(
            new[] { EquipmentGrantsPath, RollbackPath }
                .Order(StringComparer.Ordinal),
            paths.Select(property => property.Key).Order(StringComparer.Ordinal));
        Assert.Equal(
            ["get", "post"],
            Assert.IsType<JsonObject>(paths[EquipmentGrantsPath])
                .Select(property => property.Key));
        Assert.Equal(
            ["post"],
            Assert.IsType<JsonObject>(paths[RollbackPath])
                .Select(property => property.Key));

        var components = Assert.IsType<JsonObject>(root["components"]);
        var securitySchemes = Assert.IsType<JsonObject>(
            components["securitySchemes"]);
        Assert.Equal(
            ["Bearer"],
            securitySchemes.Select(property => property.Key));
        Assert.DoesNotContain(
            SwordMasterPlayerAuthentication.Scheme,
            contract,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "/api/player-assets/characters/",
            contract,
            StringComparison.Ordinal);
        Assert.DoesNotContain("/api/auth/", contract, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "/api/game-config/",
            contract,
            StringComparison.Ordinal);
        Assert.EndsWith("\n", contract, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", contract, StringComparison.Ordinal);
    }

    private static void WriteCapture(string requestedPath, string contract)
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
}
