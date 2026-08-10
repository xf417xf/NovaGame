using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using NovaCore.BuildingBlocks.ExceptionHandling;
using NovaCore.Shared.Security;
using NovaGame.Domain.Shared.Operations.AdminAccess;
using SwordMaster.Api.PlayerAssets;
using SwordMaster.Application.Contracts.PlayerAssets;
using Swashbuckle.AspNetCore.Swagger;
using Xunit;

namespace SwordMaster.Integration.Tests.PlayerAssets;

public sealed class PlayerAssetsApiTests
{
    private static readonly Guid PlayerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CharacterId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OwnedItemId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid IdempotencyKey = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnonymousAndAdministratorTokensCannotCallPlayerEndpoints(bool useAdministratorToken)
    {
        await using var factory = new SwordMasterApiFactory();
        using var client = factory.CreateHttpsClient();
        using var request = CreateRequest(
            HttpMethod.Get,
            AttributesPath,
            useAdministratorToken ? CreateAdministratorToken() : null);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(PlayerAssetsErrorCodes.UnauthorizedPlayer, await ReadErrorCodeAsync(response));
        Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
        Assert.Equal(0, factory.Probe.CallCount);
    }

    [Theory]
    [InlineData("missing-player-id")]
    [InlineData("empty-player-id")]
    [InlineData("invalid-player-id")]
    [InlineData("duplicate-player-id")]
    [InlineData("missing-token-use")]
    [InlineData("wrong-token-use")]
    [InlineData("duplicate-token-use")]
    public async Task InvalidOrMultiplePlayerClaimsAreRejected(string invalidCase)
    {
        var claims = invalidCase switch
        {
            "missing-player-id" => PlayerClaims(includePlayerId: false),
            "empty-player-id" => PlayerClaims(playerId: Guid.Empty.ToString("D")),
            "invalid-player-id" => PlayerClaims(playerId: "not-a-guid"),
            "duplicate-player-id" =>
            [
                new Claim(SecurityClaimNames.UserId, PlayerId.ToString("D")),
                new Claim(SecurityClaimNames.UserId, Guid.NewGuid().ToString("D")),
                new Claim(SwordMasterPlayerAuthentication.TokenUseClaim, SwordMasterPlayerAuthentication.PlayerTokenUse)
            ],
            "missing-token-use" => PlayerClaims(includeTokenUse: false),
            "wrong-token-use" => PlayerClaims(tokenUse: "administrator"),
            "duplicate-token-use" =>
            [
                new Claim(SecurityClaimNames.UserId, PlayerId.ToString("D")),
                new Claim(SwordMasterPlayerAuthentication.TokenUseClaim, SwordMasterPlayerAuthentication.PlayerTokenUse),
                new Claim(SwordMasterPlayerAuthentication.TokenUseClaim, SwordMasterPlayerAuthentication.PlayerTokenUse)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(invalidCase), invalidCase, null)
        };
        await using var factory = new SwordMasterApiFactory();
        using var client = factory.CreateHttpsClient();
        using var request = CreateRequest(
            HttpMethod.Get,
            AttributesPath,
            CreatePlayerToken(claims));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(PlayerAssetsErrorCodes.UnauthorizedPlayer, await ReadErrorCodeAsync(response));
        Assert.Equal(0, factory.Probe.CallCount);
    }

    [Fact]
    public async Task ValidPlayerTokenPopulatesCurrentPlayerButCannotCallAdministratorEndpoint()
    {
        await using var factory = new SwordMasterApiFactory();
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization = Bearer(CreatePlayerToken());

        using (var playerResponse = await client.GetAsync(AttributesPath))
        {
            Assert.Equal(HttpStatusCode.OK, playerResponse.StatusCode);
            var call = Assert.Single(factory.Probe.GetAttributesCalls);
            Assert.True(call.IsAuthenticated);
            Assert.Equal(PlayerId, call.PlayerId);
            Assert.Equal(CharacterId, call.CharacterId);
        }

        using var adminResponse = await client.GetAsync("/api/auth/codes");
        Assert.Equal(HttpStatusCode.Unauthorized, adminResponse.StatusCode);
        Assert.Equal(AdminAccessErrorCodes.Unauthorized, await ReadErrorCodeAsync(adminResponse));
        Assert.Single(factory.Probe.GetAttributesCalls);
    }

    [Fact]
    public async Task EquipForwardsBodyRouteAndCanonicalMutationHeadersAndReturnsStrongEtag()
    {
        await using var factory = new SwordMasterApiFactory();
        factory.Probe.MutationRevision = 8;
        using var client = factory.CreateHttpsClient();
        using var request = CreateMutationRequest(
            HttpMethod.Put,
            EquipmentPath,
            CreatePlayerToken(),
            ["\"7\""],
            [IdempotencyKey.ToString("D")],
            JsonContent.Create(new EquipEquipmentRequest(OwnedItemId)));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertSuccessfulRevisionAsync(response, 8);
        AssertStrongEtag(response, 8);
        var call = Assert.Single(factory.Probe.EquipCalls);
        Assert.True(call.IsAuthenticated);
        Assert.Equal(PlayerId, call.PlayerId);
        Assert.Equal(CharacterId, call.CharacterId);
        Assert.Equal("weapon", call.Slot);
        Assert.Equal(OwnedItemId, call.OwnedItemId);
        Assert.Equal(7, call.MutationContext.ExpectedRevision);
        Assert.Equal(IdempotencyKey, call.MutationContext.IdempotencyKey);
    }

    [Fact]
    public async Task MissingEquipRequestContentReturnsUnsupportedMediaTypeWithoutCallingApplication()
    {
        await using var factory = new SwordMasterApiFactory();
        using var client = factory.CreateHttpsClient();
        using var request = CreateMutationRequest(
            HttpMethod.Put,
            EquipmentPath,
            CreatePlayerToken(),
            ["\"7\""],
            [IdempotencyKey.ToString("D")]);

        using var response = await client.SendAsync(request);

        await AssertProblemDetailsAsync(
            response,
            HttpStatusCode.UnsupportedMediaType,
            "Unsupported Media Type");
        Assert.Equal(0, factory.Probe.CallCount);
    }

    [Fact]
    public async Task EmptyEquipRequestBodyReturnsNovaCoreValidationErrorWithoutCallingApplication()
    {
        await using var factory = new SwordMasterApiFactory();
        using var client = factory.CreateHttpsClient();
        using var request = CreateMutationRequest(
            HttpMethod.Put,
            EquipmentPath,
            CreatePlayerToken(),
            ["\"7\""],
            [IdempotencyKey.ToString("D")],
            new StringContent(string.Empty, Encoding.UTF8, "application/json"));

        using var response = await client.SendAsync(request);

        await AssertValidationErrorAsync(response, "request");
        Assert.Equal(0, factory.Probe.CallCount);
    }

    [Fact]
    public async Task MalformedOwnedItemIdReturnsNovaCoreValidationErrorWithoutCallingApplication()
    {
        await using var factory = new SwordMasterApiFactory();
        using var client = factory.CreateHttpsClient();
        using var request = CreateMutationRequest(
            HttpMethod.Put,
            EquipmentPath,
            CreatePlayerToken(),
            ["\"7\""],
            [IdempotencyKey.ToString("D")],
            new StringContent(
                "{\"ownedItemId\":\"not-a-guid\"}",
                Encoding.UTF8,
                "application/json"));

        using var response = await client.SendAsync(request);

        await AssertValidationErrorAsync(response, "$.ownedItemId");
        Assert.Equal(0, factory.Probe.CallCount);
    }

    [Fact]
    public async Task UnequipAndGetAttributesForwardThinlyAndReturnStrongEtags()
    {
        await using var factory = new SwordMasterApiFactory();
        factory.Probe.MutationRevision = 9;
        factory.Probe.AttributesRevision = 11;
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization = Bearer(CreatePlayerToken());
        using var deleteRequest = CreateMutationRequest(
            HttpMethod.Delete,
            EquipmentPath,
            bearerToken: null,
            ["\"8\""],
            [IdempotencyKey.ToString("D")]);

        using (var deleteResponse = await client.SendAsync(deleteRequest))
        {
            Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
            await AssertSuccessfulRevisionAsync(deleteResponse, 9);
            AssertStrongEtag(deleteResponse, 9);
            var call = Assert.Single(factory.Probe.UnequipCalls);
            Assert.True(call.IsAuthenticated);
            Assert.Equal(PlayerId, call.PlayerId);
            Assert.Equal(CharacterId, call.CharacterId);
            Assert.Equal("weapon", call.Slot);
            Assert.Equal(8, call.MutationContext.ExpectedRevision);
            Assert.Equal(IdempotencyKey, call.MutationContext.IdempotencyKey);
        }

        using var getResponse = await client.GetAsync(AttributesPath);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        AssertStrongEtag(getResponse, 11);
        var json = await ReadJsonAsync(getResponse);
        Assert.True(json.GetProperty("succeeded").GetBoolean());
        Assert.Equal(CharacterId, json.GetProperty("data").GetProperty("characterId").GetGuid());
        Assert.Equal(11, json.GetProperty("data").GetProperty("revision").GetInt64());
        var getCall = Assert.Single(factory.Probe.GetAttributesCalls);
        Assert.True(getCall.IsAuthenticated);
        Assert.Equal(PlayerId, getCall.PlayerId);
        Assert.Equal(CharacterId, getCall.CharacterId);
    }

    public static IEnumerable<object?[]> InvalidMutationHeaders()
    {
        var validKey = IdempotencyKey.ToString("D");
        yield return ["missing-if-match", null, new[] { validKey }];
        yield return ["weak-if-match", new[] { "W/\"7\"" }, new[] { validKey }];
        yield return ["wildcard-if-match", new[] { "*" }, new[] { validKey }];
        yield return ["unquoted-if-match", new[] { "7" }, new[] { validKey }];
        yield return ["zero-if-match", new[] { "\"0\"" }, new[] { validKey }];
        yield return ["leading-zero-if-match", new[] { "\"07\"" }, new[] { validKey }];
        yield return ["signed-if-match", new[] { "\"+7\"" }, new[] { validKey }];
        yield return ["multiple-if-match", new[] { "\"7\"", "\"8\"" }, new[] { validKey }];
        yield return ["missing-idempotency-key", new[] { "\"7\"" }, null];
        yield return ["malformed-idempotency-key", new[] { "\"7\"" }, new[] { "not-a-guid" }];
        yield return ["noncanonical-idempotency-key", new[] { "\"7\"" }, new[] { IdempotencyKey.ToString("N") }];
        yield return ["empty-idempotency-key", new[] { "\"7\"" }, new[] { Guid.Empty.ToString("D") }];
        yield return [
            "multiple-idempotency-key",
            new[] { "\"7\"" },
            new[] { validKey, Guid.NewGuid().ToString("D") }
        ];
    }

    [Theory]
    [MemberData(nameof(InvalidMutationHeaders))]
    public async Task InvalidMutationHeadersReturnStableBadRequestWithoutCallingApplication(
        string _,
        string[]? ifMatchValues,
        string[]? idempotencyValues)
    {
        await using var factory = new SwordMasterApiFactory();
        using var client = factory.CreateHttpsClient();
        using var request = CreateMutationRequest(
            HttpMethod.Put,
            EquipmentPath,
            CreatePlayerToken(),
            ifMatchValues,
            idempotencyValues,
            JsonContent.Create(new EquipEquipmentRequest(OwnedItemId)));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(PlayerAssetsErrorCodes.InvalidRequest, await ReadErrorCodeAsync(response));
        Assert.Equal(0, factory.Probe.CallCount);
    }

    public static IEnumerable<object[]> FailureMappings()
    {
        yield return [PlayerAssetsFailureKind.BadRequest, HttpStatusCode.BadRequest];
        yield return [PlayerAssetsFailureKind.Unauthorized, HttpStatusCode.Unauthorized];
        yield return [PlayerAssetsFailureKind.NotFound, HttpStatusCode.NotFound];
        yield return [PlayerAssetsFailureKind.Conflict, HttpStatusCode.Conflict];
        yield return [PlayerAssetsFailureKind.UnprocessableEntity, HttpStatusCode.UnprocessableEntity];
        yield return [PlayerAssetsFailureKind.InternalError, HttpStatusCode.InternalServerError];
    }

    [Theory]
    [MemberData(nameof(FailureMappings))]
    public async Task PlayerAssetsFailureKindsMapToStableHttpResponses(
        PlayerAssetsFailureKind failureKind,
        HttpStatusCode expectedStatus)
    {
        var errorCode = $"SwordMaster.PlayerAssets.Test.{failureKind}";
        await using var factory = new SwordMasterApiFactory();
        factory.Probe.ExceptionToThrow = new PlayerAssetsException(
            failureKind,
            errorCode,
            "Mapped test failure.");
        using var client = factory.CreateHttpsClient();
        using var request = CreateRequest(HttpMethod.Get, AttributesPath, CreatePlayerToken());

        using var response = await client.SendAsync(request);

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(errorCode, await ReadErrorCodeAsync(response));
        Assert.Single(factory.Probe.GetAttributesCalls);
        if (failureKind == PlayerAssetsFailureKind.Unauthorized)
        {
            Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
        }
    }

    [Fact]
    public async Task OpenApiDocumentsPlayerSecurityHeadersBodyEtagsAndResponsesWithoutChangingAdminSecurity()
    {
        await using var factory = new SwordMasterApiFactory();
        var swagger = factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");

        Assert.Contains("Bearer", swagger.Components.SecuritySchemes.Keys);
        Assert.Contains(SwordMasterPlayerAuthentication.Scheme, swagger.Components.SecuritySchemes.Keys);

        var equipmentPath = swagger.Paths["/api/player-assets/characters/{characterId}/equipment/{slot}"];
        var equip = equipmentPath.Operations[OperationType.Put];
        var unequip = equipmentPath.Operations[OperationType.Delete];
        var attributes = swagger.Paths["/api/player-assets/characters/{characterId}/attributes"]
            .Operations[OperationType.Get];
        AssertSecurityScheme(equip, SwordMasterPlayerAuthentication.Scheme);
        AssertSecurityScheme(unequip, SwordMasterPlayerAuthentication.Scheme);
        AssertSecurityScheme(attributes, SwordMasterPlayerAuthentication.Scheme);
        AssertSecurityScheme(
            swagger.Paths["/api/auth/codes"].Operations[OperationType.Get],
            "Bearer");

        AssertRequiredHeader(equip, PlayerAssetsController.IfMatchHeaderName);
        AssertRequiredHeader(equip, PlayerAssetsController.IdempotencyKeyHeaderName);
        AssertRequiredHeader(unequip, PlayerAssetsController.IfMatchHeaderName);
        AssertRequiredHeader(unequip, PlayerAssetsController.IdempotencyKeyHeaderName);
        Assert.DoesNotContain(
            attributes.Parameters,
            parameter => parameter.In == ParameterLocation.Header &&
                (parameter.Name == PlayerAssetsController.IfMatchHeaderName ||
                    parameter.Name == PlayerAssetsController.IdempotencyKeyHeaderName));

        var requestSchema = equip.RequestBody.Content["application/json"].Schema;
        Assert.Equal("EquipEquipmentRequest", requestSchema.Reference.Id);
        var equipRequestSchema = swagger.Components.Schemas[requestSchema.Reference.Id];
        Assert.Equal(["ownedItemId"], equipRequestSchema.Properties.Keys);

        AssertResponses(equip, "200", "400", "401", "404", "409", "422", "500");
        AssertResponses(unequip, "200", "400", "401", "404", "409", "422", "500");
        AssertResponses(attributes, "200", "400", "401", "404", "500");
        Assert.Contains("ETag", equip.Responses["200"].Headers.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ETag", unequip.Responses["200"].Headers.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ETag", attributes.Responses["200"].Headers.Keys, StringComparer.OrdinalIgnoreCase);
    }

    private static string EquipmentPath =>
        $"/api/player-assets/characters/{CharacterId:D}/equipment/weapon";

    private static string AttributesPath =>
        $"/api/player-assets/characters/{CharacterId:D}/attributes";

    private static Claim[] PlayerClaims(
        bool includePlayerId = true,
        string? playerId = null,
        bool includeTokenUse = true,
        string? tokenUse = null)
    {
        var claims = new List<Claim>();
        if (includePlayerId)
        {
            claims.Add(new Claim(
                SecurityClaimNames.UserId,
                playerId ?? PlayerId.ToString("D")));
        }
        if (includeTokenUse)
        {
            claims.Add(new Claim(
                SwordMasterPlayerAuthentication.TokenUseClaim,
                tokenUse ?? SwordMasterPlayerAuthentication.PlayerTokenUse));
        }

        return [.. claims];
    }

    private static string CreatePlayerToken(IEnumerable<Claim>? claims = null)
    {
        return CreateToken(
            SwordMasterApiFactory.PlayerIssuer,
            SwordMasterApiFactory.PlayerAudience,
            SwordMasterApiFactory.PlayerSigningKey,
            claims ?? PlayerClaims());
    }

    private static string CreateAdministratorToken()
    {
        return CreateToken(
            SwordMasterApiFactory.AdminIssuer,
            SwordMasterApiFactory.AdminAudience,
            SwordMasterApiFactory.AdminSigningKey,
            [
                new Claim(SecurityClaimNames.UserId, Guid.NewGuid().ToString("D")),
                new Claim(SecurityClaimNames.UserName, "administrator")
            ]);
    }

    private static string CreateToken(
        string issuer,
        string audience,
        string base64UrlSigningKey,
        IEnumerable<Claim> claims)
    {
        var now = DateTime.UtcNow;
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(DecodeBase64Url(base64UrlSigningKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer,
            audience,
            claims,
            now.AddMinutes(-1),
            now.AddMinutes(5),
            credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static byte[] DecodeBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        var paddingLength = (4 - (base64.Length % 4)) % 4;
        if (paddingLength > 0)
        {
            base64 += new string('=', paddingLength);
        }

        return Convert.FromBase64String(base64);
    }

    private static AuthenticationHeaderValue Bearer(string token)
    {
        return new AuthenticationHeaderValue("Bearer", token);
    }

    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        string path,
        string? bearerToken)
    {
        var request = new HttpRequestMessage(method, path);
        if (bearerToken is not null)
        {
            request.Headers.Authorization = Bearer(bearerToken);
        }

        return request;
    }

    private static HttpRequestMessage CreateMutationRequest(
        HttpMethod method,
        string path,
        string? bearerToken,
        IEnumerable<string>? ifMatchValues,
        IEnumerable<string>? idempotencyValues,
        HttpContent? content = null)
    {
        var request = CreateRequest(method, path, bearerToken);
        request.Content = content;
        if (ifMatchValues is not null)
        {
            request.Headers.TryAddWithoutValidation(
                PlayerAssetsController.IfMatchHeaderName,
                ifMatchValues);
        }
        if (idempotencyValues is not null)
        {
            request.Headers.TryAddWithoutValidation(
                PlayerAssetsController.IdempotencyKeyHeaderName,
                idempotencyValues);
        }

        return request;
    }

    private static void AssertStrongEtag(HttpResponseMessage response, long revision)
    {
        Assert.NotNull(response.Headers.ETag);
        Assert.False(response.Headers.ETag.IsWeak);
        Assert.Equal($"\"{revision}\"", response.Headers.ETag.Tag);
    }

    private static async Task AssertSuccessfulRevisionAsync(
        HttpResponseMessage response,
        long revision)
    {
        var json = await ReadJsonAsync(response);
        Assert.True(json.GetProperty("succeeded").GetBoolean());
        Assert.Equal(revision, json.GetProperty("data").GetProperty("revision").GetInt64());
    }

    private static async Task<string> ReadErrorCodeAsync(HttpResponseMessage response)
    {
        var json = await ReadJsonAsync(response);
        Assert.False(json.GetProperty("succeeded").GetBoolean());
        return json.GetProperty("error").GetProperty("code").GetString()!;
    }

    private static async Task AssertValidationErrorAsync(
        HttpResponseMessage response,
        string expectedDetailKey)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.False(json.GetProperty("succeeded").GetBoolean());
        var error = json.GetProperty("error");
        Assert.Equal(
            NovaCoreErrorCodes.ValidationError,
            error.GetProperty("code").GetString());
        Assert.Equal(
            "The request payload is invalid.",
            error.GetProperty("message").GetString());
        var details = error.GetProperty("details");
        Assert.Equal(JsonValueKind.Object, details.ValueKind);
        Assert.True(
            details.TryGetProperty(expectedDetailKey, out var messages),
            json.GetRawText());
        Assert.Equal(JsonValueKind.Array, messages.ValueKind);
        Assert.True(messages.GetArrayLength() > 0, json.GetRawText());
    }

    private static async Task AssertProblemDetailsAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedTitle)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var json = await ReadJsonAsync(response);
        Assert.Equal((int)expectedStatus, json.GetProperty("status").GetInt32());
        Assert.Equal(expectedTitle, json.GetProperty("title").GetString());
        Assert.Equal(JsonValueKind.String, json.GetProperty("type").ValueKind);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        return document.RootElement.Clone();
    }

    private static void AssertSecurityScheme(OpenApiOperation operation, string expectedScheme)
    {
        var requirement = Assert.Single(operation.Security);
        var scheme = Assert.Single(requirement.Keys);
        Assert.Equal(expectedScheme, scheme.Reference.Id);
    }

    private static void AssertRequiredHeader(OpenApiOperation operation, string headerName)
    {
        var header = Assert.Single(
            operation.Parameters,
            parameter => parameter.In == ParameterLocation.Header &&
                string.Equals(parameter.Name, headerName, StringComparison.OrdinalIgnoreCase));
        Assert.True(header.Required);
    }

    private static void AssertResponses(OpenApiOperation operation, params string[] statusCodes)
    {
        Assert.All(statusCodes, statusCode => Assert.Contains(statusCode, operation.Responses.Keys));
    }
}
