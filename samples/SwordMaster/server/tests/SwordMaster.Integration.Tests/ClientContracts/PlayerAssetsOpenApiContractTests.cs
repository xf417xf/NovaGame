using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using SwordMaster.Api.PlayerAssets;
using SwordMaster.Integration.Tests.RuntimeComposition;
using Swashbuckle.AspNetCore.Swagger;
using Xunit;

namespace SwordMaster.Integration.Tests.ClientContracts;

[Collection(SwordMasterRuntimeCompositionCollection.Name)]
public sealed class PlayerAssetsOpenApiContractTests
{
    public const string CapturePathEnvironmentVariable =
        "SWORDMASTER_PLAYER_ASSETS_OPENAPI_CAPTURE_PATH";

    [Fact]
    public void RealProgramPlayerAssetsContractMatchesTrackedArtifact()
    {
        using var factory = new SwordMasterRuntimeApiFactory();
        var source = factory.Services
            .GetRequiredService<ISwaggerProvider>()
            .GetSwagger("v1");
        var actual = PlayerAssetsOpenApiContract.CreateCanonicalJson(source);

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
            "player-assets.openapi.json");
        Assert.True(
            File.Exists(expectedPath),
            $"Tracked PlayerAssets OpenAPI contract was not copied to '{expectedPath}'.");
        var expectedBytes = File.ReadAllBytes(expectedPath);
        var actualBytes = new UTF8Encoding(false).GetBytes(actual);

        Assert.False(expectedBytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        Assert.Equal(expectedBytes, actualBytes);
    }

    private static void AssertContractShape(string contract)
    {
        var root = JsonNode.Parse(contract) as JsonObject;
        Assert.NotNull(root);

        var paths = Assert.IsType<JsonObject>(root["paths"]);
        Assert.Equal(
            new[]
            {
                PlayerAssetsOpenApiContract.AttributesPath,
                PlayerAssetsOpenApiContract.EquipmentPath
            }.Order(StringComparer.Ordinal),
            paths.Select(property => property.Key).Order(StringComparer.Ordinal));
        Assert.Equal(
            ["get"],
            Assert.IsType<JsonObject>(
                    paths[PlayerAssetsOpenApiContract.AttributesPath])
                .Select(property => property.Key));
        Assert.Equal(
            ["delete", "put"],
            Assert.IsType<JsonObject>(
                    paths[PlayerAssetsOpenApiContract.EquipmentPath])
                .Select(property => property.Key));

        var components = Assert.IsType<JsonObject>(root["components"]);
        var securitySchemes = Assert.IsType<JsonObject>(
            components["securitySchemes"]);
        Assert.Equal(
            [SwordMasterPlayerAuthentication.Scheme],
            securitySchemes.Select(property => property.Key));
        Assert.DoesNotContain("Bearer", contract, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/auth/", contract, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/game-config/", contract, StringComparison.Ordinal);
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
