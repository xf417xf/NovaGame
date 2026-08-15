using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NovaCore.BuildingBlocks.Data;
using NovaGame.Application.Contracts.Operations.AdminAccess;
using NovaGame.Domain.Shared.Operations.AdminAccess;
using Xunit;

namespace NovaGame.EntityFrameworkCore.Tests;

[Collection(MySqlDatabaseCollection.Name)]
public sealed class AdminAccessManagementApiTests
{
    private const string SeedUserName = "admin";
    private const string SeedPassword = "Test-Administrator-Password-123!";
    private const string ManagementUserAgent = "NovaGame-AdminManagementApiTests/1.0";

    private readonly MySqlDatabaseFixture _fixture;

    public AdminAccessManagementApiTests(MySqlDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ManagementAuthorizationPrecedesValidationAndRequiresAccessAndLeafPermissions()
    {
        await ResetAdminAccessAsync();
        await using var factory = CreateFactory();
        await SeedAsync(factory);

        using (var anonymousClient = factory.CreateHttpsClient(handleCookies: false))
        using (var anonymousResponse = await anonymousClient.GetAsync(
                   "/api/admin-access/administrators?page=0"))
        {
            await AssertFailureAsync(
                anonymousResponse,
                HttpStatusCode.Unauthorized,
                AdminAccessErrorCodes.Unauthorized);
            Assert.Equal("Bearer", Assert.Single(anonymousResponse.Headers.WwwAuthenticate).Scheme);
        }

        using var administratorClient = await CreateAuthenticatedClientAsync(
            factory,
            SeedUserName,
            SeedPassword);
        var accessOnlyRole = await CreateRoleAsync(
            administratorClient,
            "Access only API test role",
            [AdminAccessPermissions.Access]);
        var readOnlyWithoutAccessRole = await CreateRoleAsync(
            administratorClient,
            "Read only without access API test role",
            [AdminAccessPermissions.AdministratorsRead]);

        await CreateAdministratorAsync(
            administratorClient,
            "access-only-user",
            "Access Only User",
            "Access-Only-Password-123!",
            [accessOnlyRole.Id]);
        await CreateAdministratorAsync(
            administratorClient,
            "leaf-only-user",
            "Leaf Only User",
            "Leaf-Only-Password-123!",
            [readOnlyWithoutAccessRole.Id]);

        using var accessOnlyClient = await CreateAuthenticatedClientAsync(
            factory,
            "access-only-user",
            "Access-Only-Password-123!");
        using (var missingLeafResponse = await SendCreateRoleAsync(
                   accessOnlyClient,
                   "Rejected role",
                   [],
                   idempotencyKey: "not-a-guid"))
        {
            await AssertFailureAsync(
                missingLeafResponse,
                HttpStatusCode.Forbidden,
                AdminAccessErrorCodes.Forbidden);
        }

        using var leafOnlyClient = await CreateAuthenticatedClientAsync(
            factory,
            "leaf-only-user",
            "Leaf-Only-Password-123!");
        using var missingAccessResponse = await leafOnlyClient.GetAsync(
            "/api/admin-access/administrators?page=0");
        await AssertFailureAsync(
            missingAccessResponse,
            HttpStatusCode.Forbidden,
            AdminAccessErrorCodes.Forbidden);
    }

    [Fact]
    public async Task ManagementRoleCommandsRequireStrictHeadersAndReturnStrongEtags()
    {
        await ResetAdminAccessAsync();
        await using var factory = CreateFactory();
        await SeedAsync(factory);
        using var client = await CreateAuthenticatedClientAsync(factory, SeedUserName, SeedPassword);

        using (var missingKeyResponse = await SendCreateRoleAsync(
                   client,
                   "Missing key role",
                   [],
                   idempotencyKey: null))
        {
            await AssertFailureAsync(
                missingKeyResponse,
                HttpStatusCode.BadRequest,
                AdminAccessManagementErrorCodes.InvalidRequest);
        }

        using (var nonCanonicalKeyResponse = await SendCreateRoleAsync(
                   client,
                   "Noncanonical key role",
                   [],
                   idempotencyKey: Guid.NewGuid().ToString("N")))
        {
            await AssertFailureAsync(
                nonCanonicalKeyResponse,
                HttpStatusCode.BadRequest,
                AdminAccessManagementErrorCodes.InvalidRequest);
        }

        var duplicatedKey = Guid.NewGuid().ToString("D");
        using (var duplicateKeyRequest = CreateRoleRequest(
                   "Duplicate key header role",
                   [],
                   duplicatedKey))
        {
            duplicateKeyRequest.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("D"));
            using var duplicateKeyResponse = await client.SendAsync(duplicateKeyRequest);
            await AssertFailureAsync(
                duplicateKeyResponse,
                HttpStatusCode.BadRequest,
                AdminAccessManagementErrorCodes.InvalidRequest);
        }

        var role = await CreateRoleAsync(client, "Strong ETag role", []);
        Assert.Equal(1, role.Revision);
        Assert.Equal("\"1\"", role.ETag);

        using (var detailResponse = await client.GetAsync($"/api/admin-access/roles/{role.Id:D}"))
        {
            Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
            AssertStrongEntityTag(detailResponse, expectedRevision: 1);
            var detail = await ReadJsonAsync(detailResponse);
            AssertSucceeded(detail);
            Assert.Equal(role.Id, detail.GetProperty("data").GetProperty("id").GetGuid());
            Assert.Equal(1, detail.GetProperty("data").GetProperty("revision").GetInt64());
        }

        foreach (var invalidIfMatch in new string?[]
                 {
                     null,
                     "W/\"1\"",
                     "*",
                     "\"1\", \"2\"",
                     "\"0\"",
                     "\"-1\"",
                     "\"01\"",
                     "1"
                 })
        {
            using var invalidResponse = await SendRenameRoleAsync(
                client,
                role.Id,
                "Strong ETag role renamed",
                invalidIfMatch);
            await AssertFailureAsync(
                invalidResponse,
                HttpStatusCode.BadRequest,
                AdminAccessManagementErrorCodes.InvalidRequest);
        }

        using var renameResponse = await SendRenameRoleAsync(
            client,
            role.Id,
            "Strong ETag role renamed",
            role.ETag);
        Assert.Equal(HttpStatusCode.OK, renameResponse.StatusCode);
        AssertStrongEntityTag(renameResponse, expectedRevision: 2);
        var renamed = await ReadJsonAsync(renameResponse);
        AssertSucceeded(renamed);
        Assert.Equal("Strong ETag role renamed", renamed.GetProperty("data").GetProperty("name").GetString());
        Assert.Equal(2, renamed.GetProperty("data").GetProperty("revision").GetInt64());
    }

    [Fact]
    public async Task ManagementApiMapsStableEnvelopesAndMakesAuditRequestContextQueryable()
    {
        await ResetAdminAccessAsync();
        await using var factory = CreateFactory();
        await SeedAsync(factory);
        using var client = await CreateAuthenticatedClientAsync(factory, SeedUserName, SeedPassword);

        using (var notFoundResponse = await client.GetAsync(
                   $"/api/admin-access/roles/{Guid.NewGuid():D}"))
        {
            await AssertFailureAsync(
                notFoundResponse,
                HttpStatusCode.NotFound,
                AdminAccessManagementErrorCodes.RoleNotFound);
        }

        var correlationId = $"m35-api-{Guid.NewGuid():N}";
        var fromUtc = DateTime.UtcNow.AddMinutes(-1);
        var role = await CreateRoleAsync(
            client,
            "Envelope and audit role",
            [],
            correlationId,
            ManagementUserAgent);

        using (var duplicateResponse = await SendCreateRoleAsync(
                   client,
                   "Envelope and audit role",
                   [],
                   Guid.NewGuid().ToString("D")))
        {
            await AssertFailureAsync(
                duplicateResponse,
                HttpStatusCode.Conflict,
                AdminAccessManagementErrorCodes.RoleNameAlreadyExists);
        }

        var builtInRole = await GetBuiltInRoleAsync(client);
        using (var immutableResponse = await SendRenameRoleAsync(
                   client,
                   builtInRole.Id,
                   "Renamed built-in role",
                   builtInRole.ETag))
        {
            await AssertFailureAsync(
                immutableResponse,
                HttpStatusCode.UnprocessableEntity,
                AdminAccessManagementErrorCodes.BuiltInRoleImmutable);
        }

        var toUtc = DateTime.UtcNow.AddMinutes(1);
        var fromUtcQuery = Uri.EscapeDataString(fromUtc.ToString("O", CultureInfo.InvariantCulture));
        var toUtcQuery = Uri.EscapeDataString(toUtc.ToString("O", CultureInfo.InvariantCulture));
        var auditPath =
            $"/api/admin-access/audit-records?fromUtc={fromUtcQuery}&toUtc={toUtcQuery}&correlationId={Uri.EscapeDataString(correlationId)}&page=1&pageSize=20";
        using var auditResponse = await client.GetAsync(auditPath);
        Assert.Equal(HttpStatusCode.OK, auditResponse.StatusCode);
        var auditPage = await ReadJsonAsync(auditResponse);
        AssertSucceeded(auditPage);
        var audit = Assert.Single(
            auditPage.GetProperty("data").GetProperty("items").EnumerateArray(),
            item => item.GetProperty("roleId").GetGuid() == role.Id);
        Assert.True(audit.GetProperty("succeeded").GetBoolean());
        Assert.Equal(correlationId, audit.GetProperty("correlationId").GetString());
        Assert.Equal(ManagementUserAgent, audit.GetProperty("userAgent").GetString());
        Assert.Equal(role.Revision, audit.GetProperty("targetRevision").GetInt64());
    }

    private NovaGameApiFactory CreateFactory()
    {
        return new NovaGameApiFactory(_fixture.ConnectionString, SeedUserName, SeedPassword);
    }

    private async Task ResetAdminAccessAsync()
    {
        await using var dbContext = _fixture.CreateDbContext();
        foreach (var command in new[]
        {
            "DELETE FROM `admin_access_command_receipts`",
            "DELETE FROM `admin_audit_records`",
            "DELETE FROM `admin_refresh_token_sessions`",
            "DELETE FROM `admin_role_memberships`",
            "DELETE FROM `admin_role_permissions`",
            "DELETE FROM `admin_administrators`",
            "DELETE FROM `admin_roles`"
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

    private static async Task<HttpClient> CreateAuthenticatedClientAsync(
        NovaGameApiFactory factory,
        string userName,
        string password)
    {
        var client = factory.CreateHttpsClient(handleCookies: false);
        try
        {
            using var loginResponse = await client.PostAsJsonAsync(
                "/api/auth/login",
                new LoginRequest
                {
                    UserName = userName,
                    Password = password
                });
            Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
            var login = await ReadJsonAsync(loginResponse);
            AssertSucceeded(login);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                login.GetProperty("data").GetProperty("accessToken").GetString());
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static async Task<ManagementRole> CreateRoleAsync(
        HttpClient client,
        string name,
        IReadOnlyCollection<string> permissionNames,
        string? correlationId = null,
        string? userAgent = null)
    {
        using var response = await SendCreateRoleAsync(
            client,
            name,
            permissionNames,
            Guid.NewGuid().ToString("D"),
            correlationId,
            userAgent);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertStrongEntityTag(response, expectedRevision: 1);
        var payload = await ReadJsonAsync(response);
        AssertSucceeded(payload);
        var data = payload.GetProperty("data");
        Assert.False(data.TryGetProperty("password", out _));
        Assert.False(data.TryGetProperty("passwordHash", out _));
        return new ManagementRole(
            data.GetProperty("id").GetGuid(),
            data.GetProperty("revision").GetInt64(),
            response.Headers.ETag!.Tag!);
    }

    private static async Task CreateAdministratorAsync(
        HttpClient client,
        string userName,
        string displayName,
        string initialPassword,
        IReadOnlyCollection<Guid> roleIds)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin-access/administrators")
        {
            Content = JsonContent.Create(new
            {
                userName,
                displayName,
                initialPassword,
                roleIds = roleIds.Select(roleId => roleId.ToString("D")).ToArray()
            })
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("D"));
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertStrongEntityTag(response, expectedRevision: 1);
        AssertSucceeded(await ReadJsonAsync(response));
    }

    private static Task<HttpResponseMessage> SendCreateRoleAsync(
        HttpClient client,
        string name,
        IReadOnlyCollection<string> permissionNames,
        string? idempotencyKey,
        string? correlationId = null,
        string? userAgent = null)
    {
        var request = CreateRoleRequest(name, permissionNames, idempotencyKey, correlationId, userAgent);
        return SendAndDisposeRequestAsync(client, request);
    }

    private static async Task<HttpResponseMessage> SendAndDisposeRequestAsync(
        HttpClient client,
        HttpRequestMessage request)
    {
        using (request)
        {
            return await client.SendAsync(request);
        }
    }

    private static HttpRequestMessage CreateRoleRequest(
        string name,
        IReadOnlyCollection<string> permissionNames,
        string? idempotencyKey,
        string? correlationId = null,
        string? userAgent = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin-access/roles")
        {
            Content = JsonContent.Create(new
            {
                name,
                permissionNames = permissionNames.ToArray()
            })
        };
        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }

        if (correlationId is not null)
        {
            request.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);
        }

        if (userAgent is not null)
        {
            request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        }

        return request;
    }

    private static Task<HttpResponseMessage> SendRenameRoleAsync(
        HttpClient client,
        Guid roleId,
        string name,
        string? ifMatch)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/admin-access/roles/{roleId:D}/name")
        {
            Content = JsonContent.Create(new { name })
        };
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return SendAndDisposeRequestAsync(client, request);
    }

    private static async Task<ManagementRole> GetBuiltInRoleAsync(HttpClient client)
    {
        using var listResponse = await client.GetAsync("/api/admin-access/roles?page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await ReadJsonAsync(listResponse);
        AssertSucceeded(list);
        var roleId = Assert.Single(
            list.GetProperty("data").GetProperty("items").EnumerateArray(),
            item => item.GetProperty("isBuiltIn").GetBoolean()).GetProperty("id").GetGuid();

        using var detailResponse = await client.GetAsync($"/api/admin-access/roles/{roleId:D}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        AssertStrongEntityTag(detailResponse, expectedRevision: 1);
        var detail = await ReadJsonAsync(detailResponse);
        AssertSucceeded(detail);
        return new ManagementRole(
            roleId,
            detail.GetProperty("data").GetProperty("revision").GetInt64(),
            detailResponse.Headers.ETag!.Tag!);
    }

    private static async Task AssertFailureAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        var payload = await ReadJsonAsync(response);
        Assert.False(payload.GetProperty("succeeded").GetBoolean());
        Assert.Equal(expectedCode, payload.GetProperty("error").GetProperty("code").GetString());
    }

    private static void AssertSucceeded(JsonElement payload)
    {
        Assert.True(payload.GetProperty("succeeded").GetBoolean());
    }

    private static void AssertStrongEntityTag(HttpResponseMessage response, long expectedRevision)
    {
        Assert.NotNull(response.Headers.ETag);
        Assert.False(response.Headers.ETag.IsWeak);
        Assert.Equal($"\"{expectedRevision.ToString(CultureInfo.InvariantCulture)}\"", response.Headers.ETag.Tag);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        return document.RootElement.Clone();
    }

    private sealed record ManagementRole(Guid Id, long Revision, string ETag);
}
