using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using NovaCore.BuildingBlocks.Data;
using NovaCore.BuildingBlocks.CurrentUser;
using NovaCore.BuildingBlocks.Permissions;
using NovaCore.Shared.Security;
using NovaGame.Application.Contracts.Operations.AdminAccess;
using NovaGame.Application.Operations.AdminAccess;
using NovaGame.Domain.Operations.AdminAccess;
using NovaGame.Domain.Shared.Content.GameConfig;
using NovaGame.Domain.Shared.Operations.AdminAccess;
using Swashbuckle.AspNetCore.Swagger;
using Xunit;

namespace NovaGame.EntityFrameworkCore.Tests;

[Collection(MySqlDatabaseCollection.Name)]
public sealed class AdministratorAccessApiTests
{
    private const string SeedUserName = "admin";
    private const string SeedPassword = "Test-Administrator-Password-123!";
    private const string RefreshTokenCookieName = "novagame.admin.refresh_token";

    private readonly MySqlDatabaseFixture _fixture;

    public AdministratorAccessApiTests(MySqlDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SeedIsIdempotentAndLoginIssuesProtectedSession()
    {
        await ResetAdminAccessAsync();
        await using var factory = CreateFactory();
        await SeedAsync(factory);
        await SeedAsync(factory);

        Guid administratorId;
        await using (var dbContext = _fixture.CreateDbContext())
        {
            var administrator = Assert.Single(await dbContext.Administrators.ToArrayAsync());
            administratorId = administrator.Id;
            Assert.NotEqual(SeedPassword, administrator.PasswordHash);
            Assert.Single(await dbContext.AdminRoles.ToArrayAsync());
            Assert.Single(await dbContext.AdministratorRoles.ToArrayAsync());
            var permissions = await dbContext.RolePermissions
                .Select(permission => permission.Name)
                .OrderBy(permission => permission)
                .ToArrayAsync();
            Assert.Equal(
                new[]
                {
                    GameConfigPermissions.Access,
                    GameConfigPermissions.Activate,
                    GameConfigPermissions.ManageDrafts,
                    GameConfigPermissions.Publish,
                    AdminAccessPermissions.Access,
                    AdminAccessPermissions.AdministratorsRead,
                    AdminAccessPermissions.AdministratorsManage,
                    AdminAccessPermissions.AdministratorsResetPassword,
                    AdminAccessPermissions.RolesRead,
                    AdminAccessPermissions.RolesManage,
                    AdminAccessPermissions.SessionsRead,
                    AdminAccessPermissions.SessionsRevoke,
                    AdminAccessPermissions.AuditLogsRead
                }.OrderBy(permission => permission),
                permissions);
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            await dbContext.RolePermissions
                .Where(permission => permission.Name != AdminAccessPermissions.Access)
                .ExecuteDeleteAsync();
        }

        await SeedAsync(factory);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            Assert.IsType<HttpCurrentUser>(
                scope.ServiceProvider.GetRequiredService<ICurrentUser>());
            var snapshot = await scope.ServiceProvider
                .GetRequiredService<IAdministratorRepository>()
                .GetAuthorizationSnapshotAsync(administratorId);
            Assert.NotNull(snapshot);
            Assert.Contains(AdminAccessPermissions.Access, snapshot.PermissionNames);
            Assert.Contains(GameConfigPermissions.Access, snapshot.PermissionNames);
            Assert.Contains(GameConfigPermissions.ManageDrafts, snapshot.PermissionNames);
            Assert.Contains(GameConfigPermissions.Publish, snapshot.PermissionNames);
            Assert.Contains(GameConfigPermissions.Activate, snapshot.PermissionNames);
        }

        using var client = factory.CreateHttpsClient(handleCookies: false);
        using var loginResponse = await LoginAsync(client, SeedPassword);

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var accessToken = await ReadAccessTokenAsync(loginResponse);
        var refreshToken = GetRefreshToken(loginResponse);
        var setCookie = GetRefreshCookieHeader(loginResponse);
        Assert.Contains("HttpOnly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SameSite=Strict", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Path=/api/auth", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expires=", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            refreshToken,
            await loginResponse.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        Assert.Equal(NovaGameApiFactory.JwtIssuer, jwt.Issuer);
        Assert.Contains(NovaGameApiFactory.JwtAudience, jwt.Audiences);
        Assert.Equal(SeedUserName, jwt.Claims.Single(claim => claim.Type == SecurityClaimNames.UserName).Value);
        Assert.True(Guid.TryParse(
            jwt.Claims.Single(claim => claim.Type == SecurityClaimNames.UserId).Value,
            out _));
        Assert.Equal(TimeSpan.FromMinutes(15), jwt.ValidTo - jwt.ValidFrom);

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var session = Assert.Single(await dbContext.RefreshTokenSessions.ToArrayAsync());
            Assert.Equal(HashRefreshToken(refreshToken), session.TokenHash);
            Assert.DoesNotContain(refreshToken, Convert.ToHexString(session.TokenHash), StringComparison.Ordinal);
            Assert.Equal(DateTimeKind.Utc, session.CreatedAtUtc.Kind);
            Assert.Equal(DateTimeKind.Utc, session.ExpiresAtUtc.Kind);
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var codesResponse = await client.GetAsync("/api/auth/codes");
        Assert.Equal(HttpStatusCode.OK, codesResponse.StatusCode);
        var codes = await ReadJsonAsync(codesResponse);
        Assert.Contains(
            codes.GetProperty("data").EnumerateArray(),
            value => value.GetString() == AdminAccessPermissions.Access);

        using var userInfoResponse = await client.GetAsync("/api/user/info");
        Assert.Equal(HttpStatusCode.OK, userInfoResponse.StatusCode);
        var userInfo = await ReadJsonAsync(userInfoResponse);
        Assert.Equal(SeedUserName, userInfo.GetProperty("data").GetProperty("userName").GetString());
    }

    [Fact]
    public async Task InvalidAndInactiveLoginAttemptsAreRejectedAndAudited()
    {
        await ResetAdminAccessAsync();
        await using var factory = CreateFactory();
        await SeedAsync(factory);
        using var client = factory.CreateHttpsClient(handleCookies: false);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NovaGame-Auth-Test/1.0");
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "invalid-login-test");

        using (var missingCredentials = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { userName = string.Empty, password = string.Empty }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, missingCredentials.StatusCode);
        }

        using (var wrongPassword = await LoginAsync(client, "Wrong-Password-123!"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
            Assert.Equal(
                AdminAccessErrorCodes.InvalidCredentials,
                await ReadErrorCodeAsync(wrongPassword));
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var administrator = await dbContext.Administrators.SingleAsync();
            administrator.Deactivate(DateTime.UtcNow);
            await dbContext.SaveChangesAsync();
        }

        using (var inactive = await LoginAsync(client, SeedPassword))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, inactive.StatusCode);
            Assert.Equal(
                AdminAccessErrorCodes.InvalidCredentials,
                await ReadErrorCodeAsync(inactive));
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var failedAudits = await dbContext.AdminAccessAuditRecords
                .Where(audit => !audit.Succeeded && audit.Action == AdminAccessAuditAction.Login)
                .OrderBy(audit => audit.OccurredAtUtc)
                .ToArrayAsync();
            Assert.Equal(2, failedAudits.Length);
            Assert.Equal(AdminAccessErrorCodes.InvalidCredentials, failedAudits[0].ErrorCode);
            Assert.Equal(AdminAccessErrorCodes.AccountInactive, failedAudits[1].ErrorCode);
            Assert.Equal("NovaGame-Auth-Test/1.0", failedAudits[0].UserAgent);
            Assert.Equal("invalid-login-test", failedAudits[0].CorrelationId);
        }
    }

    [Fact]
    public async Task RefreshRotatesTokensAndReplayRevokesTheFamily()
    {
        await ResetAdminAccessAsync();
        await using var factory = CreateFactory();
        await SeedAsync(factory);
        using var client = factory.CreateHttpsClient(handleCookies: false);

        using var loginResponse = await LoginAsync(client, SeedPassword);
        var originalRefreshToken = GetRefreshToken(loginResponse);

        using var refreshResponse = await PostWithRefreshTokenAsync(
            client,
            "/api/auth/refresh",
            originalRefreshToken);
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        var replacementRefreshToken = GetRefreshToken(refreshResponse);
        Assert.NotEqual(originalRefreshToken, replacementRefreshToken);

        using var replayResponse = await PostWithRefreshTokenAsync(
            client,
            "/api/auth/refresh",
            originalRefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, replayResponse.StatusCode);
        Assert.Equal(
            AdminAccessErrorCodes.RefreshTokenReused,
            await ReadErrorCodeAsync(replayResponse));

        using var replacementResponse = await PostWithRefreshTokenAsync(
            client,
            "/api/auth/refresh",
            replacementRefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, replacementResponse.StatusCode);

        await using var dbContext = _fixture.CreateDbContext();
        var sessions = await dbContext.RefreshTokenSessions.ToArrayAsync();
        Assert.Equal(2, sessions.Length);
        Assert.All(sessions, session => Assert.True(session.IsRevoked));
        Assert.Contains(
            sessions,
            session => session.RevocationReason == RefreshTokenRevocationReason.ReuseDetected);
        Assert.Contains(
            await dbContext.AdminAccessAuditRecords.ToArrayAsync(),
            audit => audit.Action == AdminAccessAuditAction.RefreshTokenReuseDetected && !audit.Succeeded);
    }

    [Fact]
    public async Task ConcurrentRefreshAllowsOneRotationAndTreatsTheOtherAsReplay()
    {
        await ResetAdminAccessAsync();
        await using var factory = CreateFactory();
        await SeedAsync(factory);
        using var client = factory.CreateHttpsClient(handleCookies: false);

        using var loginResponse = await LoginAsync(client, SeedPassword);
        var refreshToken = GetRefreshToken(loginResponse);
        var responses = await Task.WhenAll(
            PostWithRefreshTokenAsync(client, "/api/auth/refresh", refreshToken),
            PostWithRefreshTokenAsync(client, "/api/auth/refresh", refreshToken));

        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            var replayResponse = Assert.Single(
                responses,
                response => response.StatusCode == HttpStatusCode.Unauthorized);
            Assert.Equal(
                AdminAccessErrorCodes.RefreshTokenReused,
                await ReadErrorCodeAsync(replayResponse));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        await using var dbContext = _fixture.CreateDbContext();
        Assert.All(
            await dbContext.RefreshTokenSessions.ToArrayAsync(),
            session => Assert.True(session.IsRevoked));
    }

    [Fact]
    public async Task LogoutRevokesRefreshTokenFamily()
    {
        await ResetAdminAccessAsync();
        await using var factory = CreateFactory();
        await SeedAsync(factory);
        using var client = factory.CreateHttpsClient(handleCookies: false);

        using var loginResponse = await LoginAsync(client, SeedPassword);
        var refreshToken = GetRefreshToken(loginResponse);

        using var logoutResponse = await PostWithRefreshTokenAsync(
            client,
            "/api/auth/logout",
            refreshToken);
        Assert.Equal(HttpStatusCode.OK, logoutResponse.StatusCode);

        using var refreshResponse = await PostWithRefreshTokenAsync(
            client,
            "/api/auth/refresh",
            refreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);

        await using var dbContext = _fixture.CreateDbContext();
        var session = await dbContext.RefreshTokenSessions.SingleAsync();
        Assert.Equal(RefreshTokenRevocationReason.Logout, session.RevocationReason);
    }

    [Fact]
    public async Task ProtectedEndpointsReturnStableUnauthorizedAndForbiddenResponses()
    {
        await ResetAdminAccessAsync();
        await using var factory = CreateFactory();
        await SeedAsync(factory);
        using var client = factory.CreateHttpsClient(handleCookies: false);

        using (var anonymousResponse = await client.GetAsync("/api/user/info"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
            Assert.Equal(
                AdminAccessErrorCodes.Unauthorized,
                await ReadErrorCodeAsync(anonymousResponse));
            Assert.Equal("Bearer", anonymousResponse.Headers.WwwAuthenticate.Single().Scheme);
        }

        using var loginResponse = await LoginAsync(client, SeedPassword);
        var accessToken = await ReadAccessTokenAsync(loginResponse);
        await using (var dbContext = _fixture.CreateDbContext())
        {
            await dbContext.RolePermissions.ExecuteDeleteAsync();
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var forbiddenResponse = await client.GetAsync("/api/auth/codes");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);
        Assert.Equal(
            AdminAccessErrorCodes.Forbidden,
            await ReadErrorCodeAsync(forbiddenResponse));
    }

    [Fact]
    public async Task ExpiredAccessTokenReturnsStableUnauthorizedResponse()
    {
        await ResetAdminAccessAsync();
        await using var factory = CreateFactory();
        using var client = factory.CreateHttpsClient(handleCookies: false);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateExpiredAccessToken());

        using var response = await client.GetAsync("/api/user/info");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(AdminAccessErrorCodes.Unauthorized, await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task WrongAudienceAccessTokenReturnsStableUnauthorizedResponse()
    {
        await ResetAdminAccessAsync();
        await using var factory = CreateFactory();
        using var client = factory.CreateHttpsClient(handleCookies: false);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateAccessToken("another-audience", DateTime.UtcNow.AddMinutes(5)));

        using var response = await client.GetAsync("/api/user/info");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(AdminAccessErrorCodes.Unauthorized, await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task LoginEndpointAppliesItsDedicatedRateLimit()
    {
        await ResetAdminAccessAsync();
        await using var factory = CreateFactory(loginPermitLimit: 1);
        await SeedAsync(factory);
        using var client = factory.CreateHttpsClient(handleCookies: false);

        using (var firstAttempt = await LoginAsync(client, "Wrong-Password-123!"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, firstAttempt.StatusCode);
        }

        using var secondAttempt = await LoginAsync(client, "Wrong-Password-123!");
        Assert.Equal(HttpStatusCode.TooManyRequests, secondAttempt.StatusCode);
        Assert.Equal(
            AdminAccessErrorCodes.LoginRateLimited,
            await ReadErrorCodeAsync(secondAttempt));

        using var firstHealth = await client.GetAsync("/health/live");
        using var secondHealth = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, firstHealth.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondHealth.StatusCode);
    }

    [Fact]
    public async Task SeedRequiresCompleteCredentialsAndNoOpsWhenBothAreAbsent()
    {
        await ResetAdminAccessAsync();
        await using (var incompleteFactory = new NovaGameApiFactory(
            _fixture.ConnectionString,
            seedUserName: string.Empty,
            seedPassword: SeedPassword))
        {
            await using var scope = incompleteFactory.Services.CreateAsyncScope();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                scope.ServiceProvider.GetRequiredService<IDataSeedManager>().SeedAsync());
        }

        await using (var noSeedFactory = new NovaGameApiFactory(
            _fixture.ConnectionString,
            seedUserName: string.Empty,
            seedPassword: string.Empty))
        {
            await SeedAsync(noSeedFactory);
        }

        await using var dbContext = _fixture.CreateDbContext();
        Assert.Empty(await dbContext.Administrators.ToArrayAsync());
    }

    [Fact]
    public async Task OpenApiDocumentsAnonymousAndProtectedAuthenticationContracts()
    {
        await using var factory = CreateFactory();
        var swagger = factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");

        var login = swagger.Paths["/api/auth/login"].Operations[OperationType.Post];
        Assert.True(login.Security is null || login.Security.Count == 0);

        var refresh = swagger.Paths["/api/auth/refresh"].Operations[OperationType.Post];
        Assert.True(refresh.Security is null || refresh.Security.Count == 0);
        var refreshCookie = Assert.Single(
            refresh.Parameters,
            parameter => parameter.In == ParameterLocation.Cookie);
        Assert.Equal(NovaGame.Api.Operations.AdminAccess.AdminAccessController.RefreshTokenCookieName, refreshCookie.Name);
        Assert.True(refreshCookie.Required);

        var logout = swagger.Paths["/api/auth/logout"].Operations[OperationType.Post];
        Assert.True(logout.Security is null || logout.Security.Count == 0);
        var logoutCookie = Assert.Single(
            logout.Parameters,
            parameter => parameter.In == ParameterLocation.Cookie);
        Assert.False(logoutCookie.Required);

        var permissionCodes = swagger.Paths["/api/auth/codes"].Operations[OperationType.Get];
        var securityRequirement = Assert.Single(permissionCodes.Security);
        var securityScheme = Assert.Single(securityRequirement.Keys);
        Assert.Equal("Bearer", securityScheme.Reference.Id);
    }

    private NovaGameApiFactory CreateFactory(int loginPermitLimit = 100)
    {
        return new NovaGameApiFactory(
            _fixture.ConnectionString,
            SeedUserName,
            SeedPassword,
            loginPermitLimit);
    }

    private async Task ResetAdminAccessAsync()
    {
        await using var dbContext = _fixture.CreateDbContext();
        foreach (var command in new[]
        {
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

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string password)
    {
        return client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest
            {
                UserName = SeedUserName,
                Password = password
            });
    }

    private static Task<HttpResponseMessage> PostWithRefreshTokenAsync(
        HttpClient client,
        string path,
        string refreshToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("Cookie", $"{RefreshTokenCookieName}={refreshToken}");
        return client.SendAsync(request);
    }

    private static string GetRefreshToken(HttpResponseMessage response)
    {
        var cookieHeader = GetRefreshCookieHeader(response);
        var cookie = cookieHeader.Split(';', 2)[0];
        return cookie[(RefreshTokenCookieName.Length + 1)..];
    }

    private static string GetRefreshCookieHeader(HttpResponseMessage response)
    {
        return Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith($"{RefreshTokenCookieName}=", StringComparison.Ordinal));
    }

    private static async Task<string> ReadAccessTokenAsync(HttpResponseMessage response)
    {
        var json = await ReadJsonAsync(response);
        Assert.True(json.GetProperty("succeeded").GetBoolean());
        return json.GetProperty("data").GetProperty("accessToken").GetString()!;
    }

    private static async Task<string> ReadErrorCodeAsync(HttpResponseMessage response)
    {
        var json = await ReadJsonAsync(response);
        Assert.False(json.GetProperty("succeeded").GetBoolean());
        return json.GetProperty("error").GetProperty("code").GetString()!;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        return document.RootElement.Clone();
    }

    private static byte[] HashRefreshToken(string token)
    {
        var base64 = token.Replace('-', '+').Replace('_', '/');
        var paddingLength = (4 - (base64.Length % 4)) % 4;
        if (paddingLength > 0)
        {
            base64 += new string('=', paddingLength);
        }

        return SHA256.HashData(Convert.FromBase64String(base64));
    }

    private static string CreateExpiredAccessToken()
    {
        return CreateAccessToken(
            NovaGameApiFactory.JwtAudience,
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow.AddMinutes(-2));
    }

    private static string CreateAccessToken(
        string audience,
        DateTime expiresAtUtc,
        DateTime? notBeforeUtc = null)
    {
        var now = DateTime.UtcNow;
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(
                Encoding.ASCII.GetBytes("0123456789abcdef0123456789abcdef")),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            NovaGameApiFactory.JwtIssuer,
            audience,
            new[]
            {
                new Claim(SecurityClaimNames.UserId, Guid.NewGuid().ToString("D")),
                new Claim(SecurityClaimNames.UserName, SeedUserName)
            },
            notBeforeUtc ?? now.AddMinutes(-1),
            expiresAtUtc,
            credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
