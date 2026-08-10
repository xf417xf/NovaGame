using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using NovaGame.Host.EntityFrameworkCore;
using SwordMaster.Api.PlayerAssets;
using SwordMaster.Host.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Swagger;
using Xunit;

namespace SwordMaster.Integration.Tests.RuntimeComposition;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SwordMasterRuntimeCompositionCollection
{
    public const string Name = "SwordMaster runtime composition";
}

[Collection(SwordMasterRuntimeCompositionCollection.Name)]
public sealed class SwordMasterRuntimeCompositionTests
{
    [Fact]
    public void RealProgramHostResolvesEachDbContextWithItsOwningDatabaseAndMigrations()
    {
        using var factory = new SwordMasterRuntimeApiFactory();
        using var scope = factory.Services.CreateScope();
        var novaGameDbContext = scope.ServiceProvider.GetRequiredService<NovaGameDbContext>();
        var swordMasterDbContext = scope.ServiceProvider.GetRequiredService<SwordMasterDbContext>();

        Assert.Equal(
            SwordMasterRuntimeApiFactory.NovaGameDatabaseName,
            novaGameDbContext.Database.GetDbConnection().Database);
        Assert.Equal(
            SwordMasterRuntimeApiFactory.SwordMasterDatabaseName,
            swordMasterDbContext.Database.GetDbConnection().Database);
        Assert.NotEqual(
            novaGameDbContext.Database.GetDbConnection().Database,
            swordMasterDbContext.Database.GetDbConnection().Database);

        Assert.Equal(
            typeof(NovaGameDbContext).Assembly.FullName,
            novaGameDbContext.GetService<IMigrationsAssembly>().Assembly.FullName);
        Assert.Equal(
            typeof(SwordMasterDbContext).Assembly.FullName,
            swordMasterDbContext.GetService<IMigrationsAssembly>().Assembly.FullName);
    }

    [Fact]
    public void RealProgramSwaggerContainsFrameworkAndGameRoutesWithDistinctSecuritySchemes()
    {
        using var factory = new SwordMasterRuntimeApiFactory();
        var swagger = factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");

        Assert.Contains("Bearer", swagger.Components.SecuritySchemes.Keys);
        Assert.Contains(
            SwordMasterPlayerAuthentication.Scheme,
            swagger.Components.SecuritySchemes.Keys);

        Assert.Contains("/api/auth/login", swagger.Paths.Keys);
        Assert.Contains("/api/auth/codes", swagger.Paths.Keys);
        Assert.Contains("/api/game-config/drafts", swagger.Paths.Keys);
        Assert.Contains("/api/game-config/environments/{name}/snapshot", swagger.Paths.Keys);
        Assert.Contains(
            "/api/player-assets/characters/{characterId}/equipment/{slot}",
            swagger.Paths.Keys);
        Assert.Contains(
            "/api/player-assets/characters/{characterId}/attributes",
            swagger.Paths.Keys);

        AssertSecurityScheme(
            swagger.Paths["/api/auth/codes"].Operations[OperationType.Get],
            "Bearer");
        AssertSecurityScheme(
            swagger.Paths["/api/game-config/drafts"].Operations[OperationType.Get],
            "Bearer");
        AssertSecurityScheme(
            swagger.Paths[
                "/api/player-assets/characters/{characterId}/equipment/{slot}"]
                .Operations[OperationType.Put],
            SwordMasterPlayerAuthentication.Scheme);
        AssertSecurityScheme(
            swagger.Paths[
                "/api/player-assets/characters/{characterId}/attributes"]
                .Operations[OperationType.Get],
            SwordMasterPlayerAuthentication.Scheme);
    }

    [Theory]
    [InlineData(
        "ConnectionStrings:NovaGame",
        "",
        "ConnectionStrings:NovaGame is required.")]
    [InlineData(
        "ConnectionStrings:SwordMaster",
        "",
        "ConnectionStrings:SwordMaster is required.")]
    [InlineData(
        "NovaGame:EntityFrameworkCore:ConnectionStringName",
        "Default",
        "NovaGame:EntityFrameworkCore:ConnectionStringName must be 'NovaGame'.")]
    [InlineData(
        "SwordMaster:EntityFrameworkCore:ConnectionStringName",
        "Default",
        "SwordMaster:EntityFrameworkCore:ConnectionStringName must be 'SwordMaster'.")]
    [InlineData(
        "SwordMaster:PlayerAuthentication:Jwt:SigningKey",
        "short",
        "SwordMaster:PlayerAuthentication:Jwt requires an issuer, audience, a base64url signing key containing at least 32 bytes, and a clock skew between zero and five minutes.")]
    [InlineData(
        "SwordMaster:GameConfig:Environment",
        "Development",
        "SwordMaster:GameConfig:Environment must be a lowercase safe slug.")]
    public void StartupRejectsMissingConnectionStringAndInvalidOptions(
        string key,
        string value,
        string expectedMessage)
    {
        using var factory = new SwordMasterRuntimeApiFactory(
            new Dictionary<string, string?>
            {
                [key] = value
            });

        var exception = Record.Exception(() => _ = factory.Services);

        Assert.NotNull(exception);
        Assert.Contains(expectedMessage, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void StartupRejectsConnectionStringsThatTargetTheSamePhysicalDatabase()
    {
        using var factory = new SwordMasterRuntimeApiFactory(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:SwordMaster"] =
                    "Server=127.0.0.1;Port=1;Database=novagame_runtime_composition;User ID=unused;Password=unused;Connection Timeout=1;"
            });

        var exception = Record.Exception(() => _ = factory.Services);

        Assert.NotNull(exception);
        Assert.Contains(
            "ConnectionStrings:NovaGame and ConnectionStrings:SwordMaster must target distinct MySQL databases.",
            exception.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void StartupRejectsTheSameDatabaseNameAcrossDifferentHostSpellings()
    {
        using var factory = new SwordMasterRuntimeApiFactory(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:SwordMaster"] =
                    "Server=localhost;Port=1;Database=NOVAGAME_RUNTIME_COMPOSITION;User ID=unused;Password=unused;Connection Timeout=1;"
            });

        var exception = Record.Exception(() => _ = factory.Services);

        Assert.NotNull(exception);
        Assert.Contains(
            "ConnectionStrings:NovaGame and ConnectionStrings:SwordMaster must target distinct MySQL databases.",
            exception.ToString(),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        "NovaGame:EntityFrameworkCore:ServerVersion",
        "'NovaGame:EntityFrameworkCore:ServerVersion' must target MySQL 8.4.x.")]
    [InlineData(
        "SwordMaster:EntityFrameworkCore:ServerVersion",
        "'SwordMaster:EntityFrameworkCore:ServerVersion' must target MySQL 8.4.x.")]
    public void StartupRejectsInvalidMySqlVersion(
        string key,
        string expectedMessage)
    {
        using var factory = new SwordMasterRuntimeApiFactory(
            new Dictionary<string, string?>
            {
                [key] = "8.0.36"
            });

        var exception = Record.Exception(() => _ = factory.Services);

        Assert.NotNull(exception);
        Assert.Contains(
            expectedMessage,
            exception.ToString(),
            StringComparison.Ordinal);
    }

    private static void AssertSecurityScheme(
        OpenApiOperation operation,
        string expectedScheme)
    {
        var requirement = Assert.Single(operation.Security);
        var scheme = Assert.Single(requirement.Keys);
        Assert.Equal(expectedScheme, scheme.Reference.Id);
    }
}
