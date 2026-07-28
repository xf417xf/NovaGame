using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using NovaCore.BuildingBlocks.Data;
using NovaCore.Shared.Results;
using NovaGame.Application.Contracts.Content.GameConfig;
using NovaGame.Application.Contracts.Operations.AdminAccess;
using NovaGame.Domain.Content.GameConfig;
using NovaGame.Domain.Shared.Content.GameConfig;
using NovaGame.Domain.Shared.Operations.AdminAccess;
using Swashbuckle.AspNetCore.Swagger;
using Xunit;

namespace NovaGame.EntityFrameworkCore.Tests;

[Collection(MySqlDatabaseCollection.Name)]
public sealed class GameConfigApiTests
{
    private const string SeedUserName = "admin";
    private const string SeedPassword = "Test-Administrator-Password-123!";
    private const string ConfigurationSet = "default";
    private const string EnvironmentName = "production";
    private const string TestIpAddress = "203.0.113.42";
    private const string TestUserAgent = "NovaGame-GameConfig-Test/1.0";
    private const string TestCorrelationId = "game-config-api-test";
    private const string MalformedContent = "{\"collections\":";
    private const string ForwardReferenceContent =
        "{\"collections\":{\"records\":[{\"id\":\"entry-a\",\"group\":{\"$ref\":\"groups/base\"}}],\"groups\":[{\"id\":\"base\",\"value\":10}]}}";
    private const string UpdatedForwardReferenceContent =
        "{\"collections\":{\"records\":[{\"id\":\"entry-a\",\"group\":{\"$ref\":\"groups/updated\"}}],\"groups\":[{\"id\":\"updated\",\"value\":25}]}}";

    private readonly MySqlDatabaseFixture _fixture;

    public GameConfigApiTests(MySqlDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ProtectedEndpointsRequireAuthenticationAndSpecificPermissions()
    {
        await ResetGameConfigAsync();
        await using var factory = CreateFactory();
        await SeedAsync(factory);
        using var client = factory.CreateHttpsClient(handleCookies: false);

        using (var anonymousResponse = await client.GetAsync("/api/game-config/drafts"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
            Assert.Equal(AdminAccessErrorCodes.Unauthorized, await ReadErrorCodeAsync(anonymousResponse));
        }

        await AuthenticateAsync(client);
        await using (var dbContext = _fixture.CreateDbContext())
        {
            await dbContext.RolePermissions
                .Where(permission => permission.Name == GameConfigPermissions.ManageDrafts)
                .ExecuteDeleteAsync();
        }

        try
        {
            using var forbiddenResponse = await client.PostAsJsonAsync(
                "/api/game-config/drafts",
                CreateDraftRequest("permission-test", ForwardReferenceContent));
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);
            Assert.Equal(AdminAccessErrorCodes.Forbidden, await ReadErrorCodeAsync(forbiddenResponse));
        }
        finally
        {
            await SeedAsync(factory);
        }
    }

    [Fact]
    public async Task MalformedDraftCanBeSavedAndValidationAndPublicationFailuresAreStable()
    {
        await ResetGameConfigAsync();
        await using var factory = CreateFactory();
        await SeedAsync(factory);
        using var client = factory.CreateHttpsClient(handleCookies: false);
        await AuthenticateAsync(client);

        var draft = await CreateDraftAsync(client, "malformed", MalformedContent);
        Assert.Equal(MalformedContent, draft.Content);
        Assert.Equal(1, draft.Revision);

        using (var publishResponse = await client.PostAsJsonAsync(
                   $"/api/game-config/drafts/{draft.Id:D}/publish",
                   new PublishGameConfigDraftRequest
                   {
                       ExpectedRevision = draft.Revision,
                       ReleaseNotes = "Must not publish"
                   }))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, publishResponse.StatusCode);
            Assert.Equal(GameConfigErrorCodes.DraftNotValidated, await ReadErrorCodeAsync(publishResponse));
        }

        var firstValidation = await ValidateDraftAsync(client, draft.Id, draft.Revision);
        var secondValidation = await ValidateDraftAsync(client, draft.Id, draft.Revision);
        Assert.False(firstValidation.IsValid);
        Assert.Equal(
            firstValidation.Issues.Select(ToIssueSignature),
            secondValidation.Issues.Select(ToIssueSignature));

        var issue = Assert.Single(firstValidation.Issues);
        Assert.Equal(GameConfigValidationCodes.InvalidJson, issue.Code);
        Assert.Equal(string.Empty, issue.Location);
        Assert.NotNull(issue.Line);
        Assert.NotNull(issue.Column);

        using var getResponse = await client.GetAsync($"/api/game-config/drafts/{draft.Id:D}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal(MalformedContent, (await ReadDataAsync<GameConfigDraftDto>(getResponse)).Content);
    }

    [Fact]
    public async Task PublishedSnapshotIsExactCacheableAndDraftBecomesImmutable()
    {
        await ResetGameConfigAsync();
        await using var factory = CreateFactory();
        await SeedAsync(factory);
        using var client = factory.CreateHttpsClient(handleCookies: false);
        ConfigureAuditHeaders(client);
        await AuthenticateAsync(client);

        var (draft, version) = await PublishValidVersionAsync(
            client,
            "forward-reference",
            ForwardReferenceContent,
            "Initial configuration");

        using (var repeatedPublish = await client.PostAsJsonAsync(
                   $"/api/game-config/drafts/{draft.Id:D}/publish",
                   new PublishGameConfigDraftRequest
                   {
                       ExpectedRevision = draft.Revision,
                       ReleaseNotes = "Repeated request"
                   }))
        {
            Assert.Equal(HttpStatusCode.OK, repeatedPublish.StatusCode);
            Assert.Equal(version.Id, (await ReadDataAsync<GameConfigVersionDto>(repeatedPublish)).Id);
        }

        using (var updateResponse = await client.PutAsJsonAsync(
                   $"/api/game-config/drafts/{draft.Id:D}",
                   new UpdateGameConfigDraftRequest
                   {
                       ExpectedRevision = draft.Revision,
                       Name = draft.Name,
                       SchemaVersion = draft.SchemaVersion,
                       Content = UpdatedForwardReferenceContent
                   }))
        {
            Assert.Equal(HttpStatusCode.Conflict, updateResponse.StatusCode);
            Assert.Equal(
                GameConfigErrorCodes.DraftRevisionAlreadyPublished,
                await ReadErrorCodeAsync(updateResponse));
        }

        using var snapshotResponse = await client.GetAsync(
            $"/api/game-config/versions/{version.Id:D}/snapshot");
        Assert.Equal(HttpStatusCode.OK, snapshotResponse.StatusCode);
        Assert.Equal("application/json", snapshotResponse.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(snapshotResponse.Headers.ETag);
        Assert.False(snapshotResponse.Headers.ETag.IsWeak);
        Assert.Equal($"\"{version.SnapshotHash}\"", snapshotResponse.Headers.ETag.Tag);
        Assert.True(snapshotResponse.Headers.CacheControl?.Public);
        Assert.Equal(TimeSpan.FromDays(365), snapshotResponse.Headers.CacheControl?.MaxAge);
        Assert.Contains(
            snapshotResponse.Headers.CacheControl!.Extensions,
            extension => extension.Name == "immutable");

        var snapshotBytes = await snapshotResponse.Content.ReadAsByteArrayAsync();
        await using (var dbContext = _fixture.CreateDbContext())
        {
            var storedVersion = await dbContext.PublishedGameConfigVersions
                .AsNoTracking()
                .SingleAsync(candidate => candidate.Id == version.Id);
            Assert.Equal(Encoding.UTF8.GetBytes(storedVersion.SnapshotJson), snapshotBytes);
            Assert.Equal(storedVersion.SnapshotHash, version.SnapshotHash);
        }

        Assert.Equal(
            version.SnapshotHash,
            Convert.ToHexString(SHA256.HashData(snapshotBytes)).ToLowerInvariant());

        var conditionalRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/game-config/versions/{version.Id:D}/snapshot");
        conditionalRequest.Headers.IfNoneMatch.Add(snapshotResponse.Headers.ETag);
        using var conditionalResponse = await client.SendAsync(conditionalRequest);
        Assert.Equal(HttpStatusCode.NotModified, conditionalResponse.StatusCode);
        Assert.Empty(await conditionalResponse.Content.ReadAsByteArrayAsync());
        Assert.Equal(snapshotResponse.Headers.ETag.Tag, conditionalResponse.Headers.ETag?.Tag);
    }

    [Fact]
    public async Task ActivationAndRollbackSwitchAnonymousSnapshotAndWriteAuditContext()
    {
        await ResetGameConfigAsync();
        await using var factory = CreateFactory();
        await SeedAsync(factory);
        using var client = factory.CreateHttpsClient(handleCookies: false);
        ConfigureAuditHeaders(client);
        await AuthenticateAsync(client);

        var (_, firstVersion) = await PublishValidVersionAsync(
            client,
            "version-one",
            ForwardReferenceContent,
            "Version one");
        var initialActivation = await ActivateAsync(
            client,
            firstVersion.Id,
            expectedRevision: 0,
            "Initial rollout");
        Assert.Equal(1, initialActivation.Revision);
        Assert.Equal(firstVersion.Id, initialActivation.ActiveVersionId);
        Assert.Null(initialActivation.PreviousVersionId);

        var repeatedActivation = await ActivateAsync(
            client,
            firstVersion.Id,
            expectedRevision: 0,
            "Repeated rollout");
        Assert.Equal(initialActivation, repeatedActivation);

        await Task.Delay(10);
        var (_, secondVersion) = await PublishValidVersionAsync(
            client,
            "version-two",
            UpdatedForwardReferenceContent,
            "Version two");

        using (var staleActivation = await client.PostAsJsonAsync(
                   $"/api/game-config/environments/{EnvironmentName}/activate",
                   new ActivateGameConfigVersionRequest
                   {
                       ConfigurationSet = ConfigurationSet,
                       VersionId = secondVersion.Id,
                       ExpectedRevision = 0,
                       Reason = "Stale rollout"
                   }))
        {
            Assert.Equal(HttpStatusCode.Conflict, staleActivation.StatusCode);
            Assert.Equal(GameConfigErrorCodes.ActivationConflict, await ReadErrorCodeAsync(staleActivation));
        }

        var secondActivation = await ActivateAsync(
            client,
            secondVersion.Id,
            initialActivation.Revision,
            "Second rollout");
        Assert.Equal(2, secondActivation.Revision);
        Assert.Equal(secondVersion.Id, secondActivation.ActiveVersionId);
        Assert.Equal(firstVersion.Id, secondActivation.PreviousVersionId);

        using var anonymousClient = factory.CreateHttpsClient(handleCookies: false);
        using (var activeSecond = await anonymousClient.GetAsync(
                   $"/api/game-config/environments/{EnvironmentName}/snapshot"))
        {
            Assert.Equal(HttpStatusCode.OK, activeSecond.StatusCode);
            Assert.Equal(secondVersion.SnapshotHash, activeSecond.Headers.ETag?.Tag.Trim('"'));
            Assert.True(activeSecond.Headers.CacheControl?.MustRevalidate);
            Assert.DoesNotContain(
                activeSecond.Headers.CacheControl!.Extensions,
                extension => extension.Name == "immutable");
        }

        using var rollbackResponse = await client.PostAsJsonAsync(
            $"/api/game-config/environments/{EnvironmentName}/rollback",
            new RollbackGameConfigEnvironmentRequest
            {
                ConfigurationSet = ConfigurationSet,
                TargetVersionId = firstVersion.Id,
                ExpectedRevision = secondActivation.Revision,
                Reason = "Rollback after validation"
            });
        Assert.Equal(HttpStatusCode.OK, rollbackResponse.StatusCode);
        var rollback = await ReadDataAsync<GameConfigEnvironmentDto>(rollbackResponse);
        Assert.Equal(3, rollback.Revision);
        Assert.Equal(firstVersion.Id, rollback.ActiveVersionId);
        Assert.Equal(secondVersion.Id, rollback.PreviousVersionId);
        Assert.Equal("Rollback after validation", rollback.Reason);

        using (var activeFirst = await anonymousClient.GetAsync(
                   $"/api/game-config/environments/{EnvironmentName}/snapshot"))
        {
            Assert.Equal(HttpStatusCode.OK, activeFirst.StatusCode);
            Assert.Equal(firstVersion.SnapshotHash, activeFirst.Headers.ETag?.Tag.Trim('"'));
        }

        await using var dbContext = _fixture.CreateDbContext();
        var audits = await dbContext.GameConfigAuditRecords
            .AsNoTracking()
            .OrderBy(audit => audit.OccurredAtUtc)
            .ToArrayAsync();
        Assert.Equal(5, audits.Length);
        Assert.Equal(2, audits.Count(audit => audit.Action == GameConfigAuditAction.Published));
        Assert.Equal(2, audits.Count(audit => audit.Action == GameConfigAuditAction.Activated));
        Assert.Single(audits, audit => audit.Action == GameConfigAuditAction.RolledBack);
        Assert.All(audits, audit =>
        {
            Assert.Equal(TestIpAddress, audit.IpAddress);
            Assert.Equal(TestUserAgent, audit.UserAgent);
            Assert.Equal(TestCorrelationId, audit.CorrelationId);
        });
    }

    [Fact]
    public async Task OpenApiDistinguishesAnonymousActiveSnapshotFromProtectedOperations()
    {
        await using var factory = CreateFactory();
        var swagger = factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");

        var activeSnapshot = swagger.Paths["/api/game-config/environments/{name}/snapshot"]
            .Operations[OperationType.Get];
        Assert.True(activeSnapshot.Security is null || activeSnapshot.Security.Count == 0);
        Assert.Contains("200", activeSnapshot.Responses.Keys);
        Assert.Contains("304", activeSnapshot.Responses.Keys);
        Assert.Contains("application/json", activeSnapshot.Responses["200"].Content.Keys);

        foreach (var operation in new[]
        {
            swagger.Paths["/api/game-config/drafts"].Operations[OperationType.Get],
            swagger.Paths["/api/game-config/drafts"].Operations[OperationType.Post],
            swagger.Paths["/api/game-config/versions/{versionId}/snapshot"].Operations[OperationType.Get],
            swagger.Paths["/api/game-config/environments/{name}/activate"].Operations[OperationType.Post],
            swagger.Paths["/api/game-config/environments/{name}/rollback"].Operations[OperationType.Post]
        })
        {
            var securityRequirement = Assert.Single(operation.Security);
            Assert.Equal("Bearer", Assert.Single(securityRequirement.Keys).Reference.Id);
        }
    }

    private NovaGameApiFactory CreateFactory()
    {
        return new NovaGameApiFactory(
            _fixture.ConnectionString,
            SeedUserName,
            SeedPassword);
    }

    private async Task ResetGameConfigAsync()
    {
        await using var dbContext = _fixture.CreateDbContext();
        foreach (var command in new[]
        {
            "DELETE FROM `game_config_draft_validation_issues`",
            "DELETE FROM `game_config_audit_records`",
            "DELETE FROM `game_config_environments`",
            "DELETE FROM `game_config_published_versions`",
            "DELETE FROM `game_config_drafts`"
        })
        {
            await dbContext.Database.ExecuteSqlRawAsync(command);
        }
    }

    private static async Task SeedAsync(NovaGameApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IDataSeedManager>().SeedAsync();
    }

    private static async Task AuthenticateAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest
            {
                UserName = SeedUserName,
                Password = SeedPassword
            });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = (await ReadDataAsync<AccessTokenResponse>(response)).AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private static void ConfigureAuditHeaders(HttpClient client)
    {
        client.DefaultRequestHeaders.UserAgent.ParseAdd(TestUserAgent);
        client.DefaultRequestHeaders.Add("X-Correlation-Id", TestCorrelationId);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Forwarded-For", TestIpAddress);
    }

    private static CreateGameConfigDraftRequest CreateDraftRequest(string name, string content)
    {
        return new CreateGameConfigDraftRequest
        {
            ConfigurationSet = ConfigurationSet,
            Name = name,
            SchemaVersion = "1",
            Content = content
        };
    }

    private static async Task<GameConfigDraftDto> CreateDraftAsync(
        HttpClient client,
        string name,
        string content)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/game-config/drafts",
            CreateDraftRequest(name, content));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadDataAsync<GameConfigDraftDto>(response);
    }

    private static async Task<GameConfigValidationResultDto> ValidateDraftAsync(
        HttpClient client,
        Guid draftId,
        long revision)
    {
        using var response = await client.PostAsJsonAsync(
            $"/api/game-config/drafts/{draftId:D}/validate",
            new ValidateGameConfigDraftRequest { ExpectedRevision = revision });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadDataAsync<GameConfigValidationResultDto>(response);
    }

    private static async Task<(GameConfigDraftDto Draft, GameConfigVersionDto Version)> PublishValidVersionAsync(
        HttpClient client,
        string name,
        string content,
        string releaseNotes)
    {
        var draft = await CreateDraftAsync(client, name, content);
        var validation = await ValidateDraftAsync(client, draft.Id, draft.Revision);
        Assert.True(validation.IsValid);
        Assert.Empty(validation.Issues);

        using var response = await client.PostAsJsonAsync(
            $"/api/game-config/drafts/{draft.Id:D}/publish",
            new PublishGameConfigDraftRequest
            {
                ExpectedRevision = draft.Revision,
                ReleaseNotes = releaseNotes
            });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (draft, await ReadDataAsync<GameConfigVersionDto>(response));
    }

    private static async Task<GameConfigEnvironmentDto> ActivateAsync(
        HttpClient client,
        Guid versionId,
        long expectedRevision,
        string reason)
    {
        using var response = await client.PostAsJsonAsync(
            $"/api/game-config/environments/{EnvironmentName}/activate",
            new ActivateGameConfigVersionRequest
            {
                ConfigurationSet = ConfigurationSet,
                VersionId = versionId,
                ExpectedRevision = expectedRevision,
                Reason = reason
            });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadDataAsync<GameConfigEnvironmentDto>(response);
    }

    private static string ToIssueSignature(GameConfigValidationIssueDto issue)
    {
        return $"{issue.Code}|{issue.Location}|{issue.Message}|{issue.Line}|{issue.Column}";
    }

    private static async Task<T> ReadDataAsync<T>(HttpResponseMessage response)
    {
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<T>>();
        Assert.NotNull(envelope);
        Assert.True(envelope.Succeeded);
        Assert.Null(envelope.Error);
        return Assert.IsType<T>(envelope.Data);
    }

    private static async Task<string> ReadErrorCodeAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var root = document.RootElement;
        Assert.False(root.GetProperty("succeeded").GetBoolean());
        return root.GetProperty("error").GetProperty("code").GetString()!;
    }
}
