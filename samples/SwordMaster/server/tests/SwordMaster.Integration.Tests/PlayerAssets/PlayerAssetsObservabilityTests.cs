using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using NovaCore.Shared.Results;
using NovaCore.Shared.Security;
using SwordMaster.Api.PlayerAssets;
using SwordMaster.Application.Contracts.PlayerAssets;
using SwordMaster.Integration.Tests.RuntimeComposition;
using Xunit;

namespace SwordMaster.Integration.Tests.PlayerAssets;

[Collection(SwordMasterRuntimeDatabaseCollection.Name)]
public sealed class PlayerAssetsObservabilityTests
{
    private const string PlayerIssuer = "SwordMaster.Runtime.Tests";
    private const string PlayerAudience = "SwordMaster.Player.Runtime.Tests";
    private const string PlayerSigningKey =
        "YWJjZGVmMDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODk";

    private static readonly Guid SeedPlayerId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SeedCharacterId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task RealProgramEmitsStructuredSuccessAndFailureOperationLogs()
    {
        var novaGameConnectionString = RequireEnvironmentVariable(
            "SWORDMASTER_TEST_NOVAGAME_MYSQL_CONNECTION_STRING");
        var swordMasterConnectionString = RequireEnvironmentVariable(
            "SWORDMASTER_TEST_MYSQL_CONNECTION_STRING");
        using var logCollector = new PlayerAssetsLogCollector();
        await using var factory = new SwordMasterRuntimeApiFactory(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:NovaGame"] = novaGameConnectionString,
                ["ConnectionStrings:SwordMaster"] = swordMasterConnectionString
            },
            services => services.AddSingleton<ILoggerProvider>(logCollector));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false
        });
        var playerToken = CreatePlayerToken();
        client.DefaultRequestHeaders.Authorization = Bearer(playerToken);

        const string successCorrelationId = "player-assets-observability-success";
        using var successRequest = new HttpRequestMessage(HttpMethod.Get, AttributesPath);
        successRequest.Headers.Add("X-Correlation-Id", successCorrelationId);
        using var successResponse = await client.SendAsync(successRequest);
        Assert.Equal(HttpStatusCode.OK, successResponse.StatusCode);
        var attributes = await ReadDataAsync<CharacterAttributesDto>(successResponse);

        const string failureCorrelationId = "player-assets-observability-failure";
        var staleRevision = checked(attributes.Revision + 1);
        var idempotencyKey = Guid.NewGuid();
        using var failureRequest = new HttpRequestMessage(HttpMethod.Delete, EquipmentPath);
        failureRequest.Headers.Add("X-Correlation-Id", failureCorrelationId);
        failureRequest.Headers.TryAddWithoutValidation(
            PlayerAssetsController.IfMatchHeaderName,
            $"\"{staleRevision}\"");
        failureRequest.Headers.TryAddWithoutValidation(
            PlayerAssetsController.IdempotencyKeyHeaderName,
            idempotencyKey.ToString("D"));
        using var failureResponse = await client.SendAsync(failureRequest);
        Assert.Equal(HttpStatusCode.Conflict, failureResponse.StatusCode);
        Assert.Equal(
            PlayerAssetsErrorCodes.ConcurrencyConflict,
            await ReadErrorCodeAsync(failureResponse));

        var successLog = logCollector.FindSingle(
            "PlayerAssetsOperationSucceeded",
            successCorrelationId);
        Assert.Equal(LogLevel.Information, successLog.LogLevel);
        Assert.Equal(SeedPlayerId, successLog.Get<Guid>("PlayerId"));
        Assert.Equal(SeedCharacterId, successLog.Get<Guid>("CharacterId"));
        Assert.Equal("GetAttributes", successLog.Get<string>("CommandType"));
        Assert.Equal("Success", successLog.Get<string>("ResultCode"));
        Assert.Equal(attributes.Revision, successLog.Get<long>("Revision"));

        var failureLog = logCollector.FindSingle(
            "PlayerAssetsOperationFailed",
            failureCorrelationId);
        Assert.Equal(LogLevel.Warning, failureLog.LogLevel);
        Assert.Null(failureLog.Exception);
        Assert.Equal(SeedPlayerId, failureLog.Get<Guid>("PlayerId"));
        Assert.Equal(SeedCharacterId, failureLog.Get<Guid>("CharacterId"));
        Assert.Equal("Unequip", failureLog.Get<string>("CommandType"));
        Assert.Equal(
            PlayerAssetsErrorCodes.ConcurrencyConflict,
            failureLog.Get<string>("ResultCode"));
        Assert.Equal(staleRevision, failureLog.Get<long>("Revision"));

        AssertSafeStructuredFields(successLog);
        AssertSafeStructuredFields(failureLog);
        foreach (var log in new[] { successLog, failureLog })
        {
            var rendered = log.RenderedContent;
            Assert.DoesNotContain(playerToken, rendered, StringComparison.Ordinal);
            Assert.DoesNotContain(PlayerSigningKey, rendered, StringComparison.Ordinal);
            Assert.DoesNotContain(novaGameConnectionString, rendered, StringComparison.Ordinal);
            Assert.DoesNotContain(swordMasterConnectionString, rendered, StringComparison.Ordinal);
            Assert.DoesNotContain(idempotencyKey.ToString("D"), rendered, StringComparison.Ordinal);
        }
    }

    private static void AssertSafeStructuredFields(PlayerAssetsLogEntry log)
    {
        Assert.Equal(
            [
                "CharacterId",
                "CommandType",
                "CorrelationId",
                "PlayerId",
                "ResultCode",
                "Revision",
                "{OriginalFormat}"
            ],
            log.Properties.Keys.OrderBy(key => key, StringComparer.Ordinal));
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
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse>();
        Assert.NotNull(envelope);
        Assert.False(envelope.Succeeded);
        Assert.NotNull(envelope.Error);
        return envelope.Error.Code;
    }

    private static string CreatePlayerToken()
    {
        var now = DateTime.UtcNow;
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(DecodeBase64Url(PlayerSigningKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            PlayerIssuer,
            PlayerAudience,
            [
                new Claim(SecurityClaimNames.UserId, SeedPlayerId.ToString("D")),
                new Claim(
                    SwordMasterPlayerAuthentication.TokenUseClaim,
                    SwordMasterPlayerAuthentication.PlayerTokenUse)
            ],
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

    private static string RequireEnvironmentVariable(string name)
    {
        return Environment.GetEnvironmentVariable(name) ??
            throw new InvalidOperationException(
                $"{name} is required for SwordMaster observability integration tests.");
    }

    private static string EquipmentPath =>
        $"/api/player-assets/characters/{SeedCharacterId:D}/equipment/weapon";

    private static string AttributesPath =>
        $"/api/player-assets/characters/{SeedCharacterId:D}/attributes";

    private sealed class PlayerAssetsLogCollector : ILoggerProvider
    {
        private readonly ConcurrentQueue<PlayerAssetsLogEntry> _entries = new();

        public ILogger CreateLogger(string categoryName)
        {
            return new CollectingLogger(categoryName, _entries);
        }

        public PlayerAssetsLogEntry FindSingle(string eventName, string correlationId)
        {
            return Assert.Single(_entries, entry =>
                string.Equals(entry.EventId.Name, eventName, StringComparison.Ordinal) &&
                string.Equals(
                    entry.Get<string>("CorrelationId"),
                    correlationId,
                    StringComparison.Ordinal));
        }

        public void Dispose()
        {
        }
    }

    private sealed class CollectingLogger(
        string categoryName,
        ConcurrentQueue<PlayerAssetsLogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel != LogLevel.None;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!string.Equals(
                    categoryName,
                    typeof(PlayerAssetsOperationLogger).FullName,
                    StringComparison.Ordinal) ||
                state is not IEnumerable<KeyValuePair<string, object?>> structuredState)
            {
                return;
            }

            entries.Enqueue(new PlayerAssetsLogEntry(
                logLevel,
                eventId,
                structuredState.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.Ordinal),
                formatter(state, exception),
                exception));
        }
    }

    private sealed record PlayerAssetsLogEntry(
        LogLevel LogLevel,
        EventId EventId,
        IReadOnlyDictionary<string, object?> Properties,
        string Message,
        Exception? Exception)
    {
        public T Get<T>(string name)
        {
            Assert.True(Properties.TryGetValue(name, out var value));
            return Assert.IsType<T>(value);
        }

        public string RenderedContent => string.Join(
            " ",
            Properties.Values.Prepend(Message).Select(value => value?.ToString()));
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
