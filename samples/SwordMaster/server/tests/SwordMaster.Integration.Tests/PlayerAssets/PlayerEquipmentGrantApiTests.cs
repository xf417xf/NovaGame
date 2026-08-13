using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using NovaCore.BuildingBlocks.Correlation;
using NovaCore.BuildingBlocks.ExceptionHandling;
using NovaCore.Shared.Security;
using SwordMaster.Api.PlayerAssets;
using SwordMaster.Application.Contracts.PlayerAssets;
using SwordMaster.Application.PlayerAssets;
using Xunit;

namespace SwordMaster.Integration.Tests.PlayerAssets;

public sealed class PlayerEquipmentGrantApiTests
{
    private const string IfMatchHeaderName = "If-Match";
    private const string IdempotencyKeyHeaderName = "Idempotency-Key";

    private static readonly Guid AdministratorId =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PlayerId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid GrantId =
        Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid IdempotencyKey =
        Guid.Parse("abcdefab-cdef-abcd-efab-cdefabcdefab");

    [Theory]
    [InlineData("get")]
    [InlineData("grant")]
    [InlineData("rollback")]
    public async Task AnonymousAndPlayerCredentialsCannotCallAdministratorOperations(
        string operation)
    {
        foreach (var token in new string?[] { null, CreatePlayerToken() })
        {
            await using var factory = new SwordMasterApiFactory();
            using var client = factory.CreateHttpsClient();
            using var request = CreateOperationRequest(operation, token);

            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(
                PlayerEquipmentGrantErrorCodes.UnauthorizedAdministrator,
                await ReadErrorCodeAsync(response));
            Assert.Equal(
                "Bearer",
                Assert.Single(response.Headers.WwwAuthenticate).Scheme);
            Assert.Equal(0, factory.EquipmentGrantProbe.CallCount);
        }
    }

    [Theory]
    [InlineData("get")]
    [InlineData("grant")]
    [InlineData("rollback")]
    public async Task AdministratorWithoutPermissionIsRejectedBeforeApplication(
        string operation)
    {
        await using var factory = new SwordMasterApiFactory();
        factory.EquipmentGrantPermissionChecker.IsGranted = false;
        using var client = factory.CreateHttpsClient();
        using var request = CreateOperationRequest(
            operation,
            CreateAdministratorToken());

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(
            PlayerEquipmentGrantErrorCodes.Forbidden,
            await ReadErrorCodeAsync(response));
        Assert.Equal(0, factory.EquipmentGrantProbe.CallCount);
        Assert.Contains(
            PlayerEquipmentGrantPermissions.Manage,
            factory.EquipmentGrantPermissionChecker.PermissionChecks);
    }

    [Fact]
    public async Task GetForwardsDefaultAndExplicitPagingWithAdministratorContextAndStrongEtag()
    {
        await using var factory = new SwordMasterApiFactory();
        factory.EquipmentGrantProbe.PageResult =
            factory.EquipmentGrantProbe.PageResult with { Revision = 17 };
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization = Bearer(
            CreateAdministratorToken());

        using (var defaultRequest = new HttpRequestMessage(
                   HttpMethod.Get,
                   EquipmentGrantsPath))
        {
            defaultRequest.Headers.Add("X-Correlation-Id", "equipment-grant-get-default");
            defaultRequest.Headers.UserAgent.ParseAdd("NovaGame-T03-Tests/1.0");
            using var defaultResponse = await client.SendAsync(defaultRequest);

            Assert.Equal(HttpStatusCode.OK, defaultResponse.StatusCode);
            var result = await ReadDataAsync<PlayerEquipmentGrantPageDto>(defaultResponse);
            Assert.Equal(17, result.Revision);
            AssertStrongEtag(defaultResponse, 17);
        }

        using (var explicitResponse = await client.GetAsync(
                   $"{EquipmentGrantsPath}?page=3&pageSize=25"))
        {
            Assert.Equal(HttpStatusCode.OK, explicitResponse.StatusCode);
            AssertStrongEtag(explicitResponse, 17);
        }

        Assert.Collection(
            factory.EquipmentGrantProbe.GetPageCalls,
            call =>
            {
                Assert.Equal(PlayerId, call.PlayerId);
                Assert.Equal(1, call.Page);
                Assert.Equal(20, call.PageSize);
                AssertAdministrator(call.HttpContext);
                Assert.Equal(
                    "equipment-grant-get-default",
                    call.HttpContext.AuditContext.CorrelationId);
                Assert.Equal(
                    "NovaGame-T03-Tests/1.0",
                    call.HttpContext.AuditContext.UserAgent);
            },
            call =>
            {
                Assert.Equal(PlayerId, call.PlayerId);
                Assert.Equal(3, call.Page);
                Assert.Equal(25, call.PageSize);
                AssertAdministrator(call.HttpContext);
            });
    }

    [Fact]
    public async Task GrantForwardsRouteBodyHeadersAndAuditContextAndReturnsStrongEtag()
    {
        await using var factory = new SwordMasterApiFactory();
        factory.EquipmentGrantProbe.GrantResult =
            factory.EquipmentGrantProbe.GrantResult with
            {
                PlayerId = PlayerId,
                ResultingRevision = 18
            };
        using var client = factory.CreateHttpsClient();
        var body = new GrantPlayerEquipmentRequest(
            "starter-sword",
            "Customer support correction");
        using var request = CreateMutationRequest(
            HttpMethod.Post,
            EquipmentGrantsPath,
            CreateAdministratorToken(),
            ["\"17\""],
            [IdempotencyKey.ToString("D")],
            JsonContent.Create(body));
        request.Headers.Add("X-Correlation-Id", "equipment-grant-create");
        request.Headers.UserAgent.ParseAdd("NovaGame-T03-Tests/1.0");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await ReadDataAsync<PlayerEquipmentGrantMutationResultDto>(response);
        Assert.Equal(18, result.ResultingRevision);
        Assert.Equal(PlayerEquipmentGrantStatusDto.Active, result.Status);
        AssertStrongEtag(response, 18);
        var call = Assert.Single(factory.EquipmentGrantProbe.GrantCalls);
        Assert.Equal(PlayerId, call.PlayerId);
        Assert.Equal(body, call.Request);
        Assert.Equal(17, call.MutationContext.ExpectedRevision);
        Assert.Equal(IdempotencyKey, call.MutationContext.IdempotencyKey);
        AssertAdministrator(call.HttpContext);
        Assert.Equal(
            "equipment-grant-create",
            call.HttpContext.AuditContext.CorrelationId);
        Assert.Equal(
            "NovaGame-T03-Tests/1.0",
            call.HttpContext.AuditContext.UserAgent);
    }

    [Fact]
    public async Task RollbackForwardsRoutesBodyHeadersAndAuditContextAndReturnsStrongEtag()
    {
        await using var factory = new SwordMasterApiFactory();
        factory.EquipmentGrantProbe.RollbackResult =
            factory.EquipmentGrantProbe.RollbackResult with
            {
                PlayerId = PlayerId,
                GrantId = GrantId,
                ResultingRevision = 19
            };
        using var client = factory.CreateHttpsClient();
        var body = new RollbackPlayerEquipmentGrantRequest(
            "Correction approved by support lead");
        using var request = CreateMutationRequest(
            HttpMethod.Post,
            RollbackPath,
            CreateAdministratorToken(),
            ["\"18\""],
            [IdempotencyKey.ToString("D")],
            JsonContent.Create(body));
        request.Headers.Add("X-Correlation-Id", "equipment-grant-rollback");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await ReadDataAsync<PlayerEquipmentGrantMutationResultDto>(response);
        Assert.Equal(19, result.ResultingRevision);
        Assert.Equal(PlayerEquipmentGrantStatusDto.RolledBack, result.Status);
        AssertStrongEtag(response, 19);
        var call = Assert.Single(factory.EquipmentGrantProbe.RollbackCalls);
        Assert.Equal(PlayerId, call.PlayerId);
        Assert.Equal(GrantId, call.GrantId);
        Assert.Equal(body, call.Request);
        Assert.Equal(18, call.MutationContext.ExpectedRevision);
        Assert.Equal(IdempotencyKey, call.MutationContext.IdempotencyKey);
        AssertAdministrator(call.HttpContext);
        Assert.Equal(
            "equipment-grant-rollback",
            call.HttpContext.AuditContext.CorrelationId);
    }

    public static IEnumerable<object?[]> InvalidMutationHeaders()
    {
        var validKey = IdempotencyKey.ToString("D");
        yield return ["missing-if-match", null, new[] { validKey }];
        yield return ["weak-if-match", new[] { "W/\"17\"" }, new[] { validKey }];
        yield return ["wildcard-if-match", new[] { "*" }, new[] { validKey }];
        yield return ["unquoted-if-match", new[] { "17" }, new[] { validKey }];
        yield return ["zero-if-match", new[] { "\"0\"" }, new[] { validKey }];
        yield return ["leading-zero-if-match", new[] { "\"017\"" }, new[] { validKey }];
        yield return ["signed-if-match", new[] { "\"+17\"" }, new[] { validKey }];
        yield return [
            "overflow-if-match",
            new[] { "\"9223372036854775808\"" },
            new[] { validKey }
        ];
        yield return [
            "multiple-if-match",
            new[] { "\"17\"", "\"18\"" },
            new[] { validKey }
        ];
        yield return ["missing-idempotency-key", new[] { "\"17\"" }, null];
        yield return [
            "malformed-idempotency-key",
            new[] { "\"17\"" },
            new[] { "not-a-guid" }
        ];
        yield return [
            "noncanonical-idempotency-key",
            new[] { "\"17\"" },
            new[] { IdempotencyKey.ToString("N") }
        ];
        yield return [
            "uppercase-idempotency-key",
            new[] { "\"17\"" },
            new[] { validKey.ToUpperInvariant() }
        ];
        yield return [
            "empty-idempotency-key",
            new[] { "\"17\"" },
            new[] { Guid.Empty.ToString("D") }
        ];
        yield return [
            "multiple-idempotency-key",
            new[] { "\"17\"" },
            new[] { validKey, Guid.NewGuid().ToString("D") }
        ];
    }

    [Theory]
    [MemberData(nameof(InvalidMutationHeaders))]
    public async Task InvalidMutationHeadersReturnStableBadRequestBeforeApplication(
        string _,
        string[]? ifMatchValues,
        string[]? idempotencyValues)
    {
        await using var factory = new SwordMasterApiFactory();
        using var client = factory.CreateHttpsClient();
        using var request = CreateMutationRequest(
            HttpMethod.Post,
            EquipmentGrantsPath,
            CreateAdministratorToken(),
            ifMatchValues,
            idempotencyValues,
            JsonContent.Create(new GrantPlayerEquipmentRequest(
                "starter-sword",
                "Customer support correction")));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            PlayerEquipmentGrantErrorCodes.InvalidRequest,
            await ReadErrorCodeAsync(response));
        Assert.Equal(0, factory.EquipmentGrantProbe.CallCount);
    }

    [Fact]
    public async Task RollbackUsesTheSameCanonicalMutationHeaderContract()
    {
        await using var factory = new SwordMasterApiFactory();
        using var client = factory.CreateHttpsClient();
        using var request = CreateMutationRequest(
            HttpMethod.Post,
            RollbackPath,
            CreateAdministratorToken(),
            ["\"18\""],
            [IdempotencyKey.ToString("D").ToUpperInvariant()],
            JsonContent.Create(new RollbackPlayerEquipmentGrantRequest(
                "Correction approved by support lead")));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            PlayerEquipmentGrantErrorCodes.InvalidRequest,
            await ReadErrorCodeAsync(response));
        Assert.Equal(0, factory.EquipmentGrantProbe.CallCount);
    }

    [Theory]
    [InlineData("malformed-json")]
    [InlineData("empty-json")]
    [InlineData("unsupported-media-type")]
    [InlineData("non-integer-page")]
    [InlineData("malformed-player-id")]
    [InlineData("malformed-grant-id")]
    public async Task TransportAndBindingFailuresUseFeatureInvalidRequestWithoutCallingApplication(
        string invalidCase)
    {
        var logCollector = new EquipmentGrantLogCollector();
        await using var factory = new SwordMasterApiFactory(
            services => services.AddSingleton<ILoggerProvider>(logCollector));
        using var client = factory.CreateHttpsClient();
        var accessToken = CreateAdministratorToken();
        var correlationId = $"equipment-grant-invalid-{invalidCase}";
        using var request = CreateInvalidRequest(invalidCase, accessToken);
        request.Headers.Add("X-Correlation-Id", correlationId);
        request.Headers.UserAgent.ParseAdd("Invalid-Request-Sensitive-Agent/1.0");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            PlayerEquipmentGrantErrorCodes.InvalidRequest,
            await ReadErrorCodeAsync(response));
        Assert.Equal(0, factory.EquipmentGrantProbe.CallCount);

        var log = logCollector.FindSingle(correlationId);
        Assert.Equal(LogLevel.Warning, log.LogLevel);
        Assert.Equal(
            "PlayerEquipmentGrantOperationFailed",
            log.EventId.Name);
        Assert.Equal(AdministratorId, log.Get<Guid>("AdministratorId"));
        Assert.Equal(
            PlayerEquipmentGrantErrorCodes.InvalidRequest,
            log.Get<string>("ResultCode"));
        Assert.Equal(ExpectedCommandType(invalidCase), log.Get<string>("CommandType"));
        Assert.Equal(
            ExpectedLoggedPlayerId(invalidCase),
            log.Properties["PlayerId"]);
        Assert.Equal(
            ExpectedLoggedGrantId(invalidCase),
            log.Properties["GrantId"]);
        AssertSafeLogFields(log);
        Assert.DoesNotContain(
            "do-not-log-invalid-request-body",
            log.RenderedContent,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            accessToken,
            log.RenderedContent,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            IdempotencyKey.ToString("D"),
            log.RenderedContent,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Invalid-Request-Sensitive-Agent/1.0",
            log.RenderedContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmptyPlayerIdUsesApplicationSemanticValidation()
    {
        const string correlationId = "equipment-grant-empty-player-id";
        var logCollector = new EquipmentGrantLogCollector();
        await using var factory = new SwordMasterApiFactory(
            services => services.AddSingleton<ILoggerProvider>(logCollector));
        factory.EquipmentGrantProbe.ExceptionToThrow =
            new PlayerEquipmentGrantException(
                PlayerEquipmentGrantFailureKind.BadRequest,
                PlayerEquipmentGrantErrorCodes.InvalidRequest,
                "A player identifier is required.");
        using var client = factory.CreateHttpsClient();
        using var request = CreateRequest(
            HttpMethod.Get,
            "/api/swordmaster/operations/player-assets/00000000-0000-0000-0000-000000000000/equipment-grants",
            CreateAdministratorToken());
        request.Headers.Add("X-Correlation-Id", correlationId);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            PlayerEquipmentGrantErrorCodes.InvalidRequest,
            await ReadErrorCodeAsync(response));
        var call = Assert.Single(factory.EquipmentGrantProbe.GetPageCalls);
        Assert.Equal(Guid.Empty, call.PlayerId);

        var log = logCollector.FindSingle(correlationId);
        Assert.Equal(LogLevel.Warning, log.LogLevel);
        Assert.Null(log.Properties["PlayerId"]);
        Assert.Equal(
            PlayerEquipmentGrantErrorCodes.InvalidRequest,
            log.Get<string>("ResultCode"));
        AssertSafeLogFields(log);
    }

    public static IEnumerable<object[]> FailureMappings()
    {
        yield return [PlayerEquipmentGrantFailureKind.BadRequest, HttpStatusCode.BadRequest];
        yield return [PlayerEquipmentGrantFailureKind.Unauthorized, HttpStatusCode.Unauthorized];
        yield return [PlayerEquipmentGrantFailureKind.Forbidden, HttpStatusCode.Forbidden];
        yield return [PlayerEquipmentGrantFailureKind.NotFound, HttpStatusCode.NotFound];
        yield return [PlayerEquipmentGrantFailureKind.Conflict, HttpStatusCode.Conflict];
        yield return [
            PlayerEquipmentGrantFailureKind.UnprocessableEntity,
            HttpStatusCode.UnprocessableEntity
        ];
    }

    [Theory]
    [MemberData(nameof(FailureMappings))]
    public async Task FailureKindsMapToStableHttpResponsesAndPreserveDetails(
        PlayerEquipmentGrantFailureKind failureKind,
        HttpStatusCode expectedStatus)
    {
        var errorCode = $"SwordMaster.PlayerAssets.EquipmentGrants.Test.{failureKind}";
        await using var factory = new SwordMasterApiFactory();
        factory.EquipmentGrantProbe.ExceptionToThrow =
            new PlayerEquipmentGrantException(
                failureKind,
                errorCode,
                "Mapped equipment-grant failure.",
                new Dictionary<string, string[]>
                {
                    ["field"] = ["Mapped detail."]
                });
        using var client = factory.CreateHttpsClient();
        using var request = CreateOperationRequest(
            "get",
            CreateAdministratorToken());

        using var response = await client.SendAsync(request);

        Assert.Equal(expectedStatus, response.StatusCode);
        var error = await ReadErrorAsync(response);
        Assert.Equal(errorCode, error.GetProperty("code").GetString());
        Assert.Equal(
            "Mapped detail.",
            error.GetProperty("details").GetProperty("field")[0].GetString());
        Assert.Single(factory.EquipmentGrantProbe.GetPageCalls);
        if (failureKind == PlayerEquipmentGrantFailureKind.Unauthorized)
        {
            Assert.Equal(
                "Bearer",
                Assert.Single(response.Headers.WwwAuthenticate).Scheme);
        }
    }

    [Fact]
    public async Task UnknownExceptionsReturnSanitizedInternalServerError()
    {
        const string sensitiveMessage =
            "do-not-expose-equipment-grant-internal-details";
        await using var factory = new SwordMasterApiFactory();
        factory.EquipmentGrantProbe.ExceptionToThrow =
            new InvalidOperationException(sensitiveMessage);
        using var client = factory.CreateHttpsClient();
        using var request = CreateOperationRequest(
            "get",
            CreateAdministratorToken());

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal(
            NovaCoreErrorCodes.InternalServerError,
            json.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain(
            sensitiveMessage,
            json.GetRawText(),
            StringComparison.Ordinal);
        Assert.Single(factory.EquipmentGrantProbe.GetPageCalls);
    }

    [Fact]
    public async Task TypedInternalErrorsReturnSanitizedInternalEnvelopeAndSafeLog()
    {
        const string sensitiveCode =
            "SwordMaster.PlayerAssets.EquipmentGrants.SensitiveInternalCode";
        const string sensitiveMessage =
            "do-not-expose-typed-internal-message";
        const string sensitiveDetail =
            "do-not-expose-typed-internal-detail";
        const string correlationId = "equipment-grant-typed-internal";
        var logCollector = new EquipmentGrantLogCollector();
        await using var factory = new SwordMasterApiFactory(
            services => services.AddSingleton<ILoggerProvider>(logCollector));
        factory.EquipmentGrantProbe.ExceptionToThrow =
            new PlayerEquipmentGrantException(
                PlayerEquipmentGrantFailureKind.InternalError,
                sensitiveCode,
                sensitiveMessage,
                new Dictionary<string, string[]>
                {
                    ["internal"] = [sensitiveDetail]
                });
        using var client = factory.CreateHttpsClient();
        using var request = CreateOperationRequest(
            "get",
            CreateAdministratorToken());
        request.Headers.Add("X-Correlation-Id", correlationId);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var json = await ReadJsonAsync(response);
        var error = json.GetProperty("error");
        Assert.Equal(
            NovaCoreErrorCodes.InternalServerError,
            error.GetProperty("code").GetString());
        Assert.Equal(
            "An internal server error occurred.",
            error.GetProperty("message").GetString());
        Assert.DoesNotContain(sensitiveCode, json.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(sensitiveMessage, json.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(sensitiveDetail, json.GetRawText(), StringComparison.Ordinal);
        Assert.Single(factory.EquipmentGrantProbe.GetPageCalls);

        var log = logCollector.FindSingle(correlationId);
        Assert.Equal(LogLevel.Error, log.LogLevel);
        Assert.Equal(
            NovaCoreErrorCodes.InternalServerError,
            log.Get<string>("ResultCode"));
        AssertSafeLogFields(log);
        Assert.DoesNotContain(
            sensitiveCode,
            log.RenderedContent,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            sensitiveMessage,
            log.RenderedContent,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            sensitiveDetail,
            log.RenderedContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RequestAbortedCancellationRemainsUnhandledAndUnloggedByFeatureFilter()
    {
        var logCollector = new EquipmentGrantLogCollector();
        using var loggerFactory = LoggerFactory.Create(
            builder => builder.AddProvider(logCollector));
        var operationLogger = new PlayerEquipmentGrantOperationLogger(
            new TestCorrelationIdAccessor
            {
                CorrelationId = "equipment-grant-cancelled"
            },
            new TestCurrentAdministrator(AdministratorId),
            loggerFactory.CreateLogger<PlayerEquipmentGrantOperationLogger>());
        var filter = new PlayerEquipmentGrantExceptionFilter(operationLogger);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var httpContext = new DefaultHttpContext
        {
            RequestAborted = cancellation.Token
        };
        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ControllerActionDescriptor
            {
                ControllerTypeInfo =
                    typeof(PlayerEquipmentGrantsController).GetTypeInfo()
            },
            new ModelStateDictionary());
        var exceptionContext = new ExceptionContext(actionContext, [])
        {
            Exception = new OperationCanceledException(cancellation.Token)
        };

        filter.OnException(exceptionContext);

        Assert.False(exceptionContext.ExceptionHandled);
        Assert.Null(exceptionContext.Result);
        Assert.Equal(0, logCollector.Count);
    }

    [Fact]
    public async Task SuccessFailureAndDenialEmitSafeStructuredOperationLogs()
    {
        var logCollector = new EquipmentGrantLogCollector();
        await using var factory = new SwordMasterApiFactory(
            services => services.AddSingleton<ILoggerProvider>(logCollector));
        using var client = factory.CreateHttpsClient();
        var accessToken = CreateAdministratorToken();
        const string userAgent = "Equipment-Grant-Sensitive-Agent/1.0";

        using (var successRequest = CreateMutationRequest(
                   HttpMethod.Post,
                   EquipmentGrantsPath,
                   accessToken,
                   ["\"7\""],
                   [IdempotencyKey.ToString("D")],
                   JsonContent.Create(new GrantPlayerEquipmentRequest(
                       "starter-sword",
                       "Sensitive success reason"))))
        {
            successRequest.Headers.Add("X-Correlation-Id", "grant-log-success");
            successRequest.Headers.UserAgent.ParseAdd(userAgent);
            using var successResponse = await client.SendAsync(successRequest);
            Assert.Equal(HttpStatusCode.OK, successResponse.StatusCode);
        }

        factory.EquipmentGrantProbe.ExceptionToThrow =
            new PlayerEquipmentGrantException(
                PlayerEquipmentGrantFailureKind.Conflict,
                PlayerEquipmentGrantErrorCodes.ConcurrencyConflict,
                "The supplied revision is stale.");
        using (var failureRequest = CreateMutationRequest(
                   HttpMethod.Post,
                   RollbackPath,
                   accessToken,
                   ["\"8\""],
                   [Guid.NewGuid().ToString("D")],
                   JsonContent.Create(new RollbackPlayerEquipmentGrantRequest(
                       "Sensitive rollback reason"))))
        {
            failureRequest.Headers.Add("X-Correlation-Id", "grant-log-failure");
            failureRequest.Headers.UserAgent.ParseAdd(userAgent);
            using var failureResponse = await client.SendAsync(failureRequest);
            Assert.Equal(HttpStatusCode.Conflict, failureResponse.StatusCode);
        }

        factory.EquipmentGrantPermissionChecker.IsGranted = false;
        using (var deniedRequest = CreateRequest(
                   HttpMethod.Get,
                   EquipmentGrantsPath,
                   accessToken))
        {
            deniedRequest.Headers.Add("X-Correlation-Id", "grant-log-denied");
            deniedRequest.Headers.UserAgent.ParseAdd(userAgent);
            using var deniedResponse = await client.SendAsync(deniedRequest);
            Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);
        }

        var success = logCollector.FindSingle("grant-log-success");
        Assert.Equal(LogLevel.Information, success.LogLevel);
        Assert.Equal(AdministratorId, success.Get<Guid>("AdministratorId"));
        Assert.Equal(PlayerId, success.Get<Guid>("PlayerId"));
        Assert.Equal(GrantId, success.Get<Guid>("GrantId"));
        Assert.Equal("Grant", success.Get<string>("CommandType"));
        Assert.Equal("Success", success.Get<string>("ResultCode"));
        Assert.Equal(8, success.Get<long>("Revision"));

        var failure = logCollector.FindSingle("grant-log-failure");
        Assert.Equal(LogLevel.Warning, failure.LogLevel);
        Assert.Equal(AdministratorId, failure.Get<Guid>("AdministratorId"));
        Assert.Equal(PlayerId, failure.Get<Guid>("PlayerId"));
        Assert.Equal(GrantId, failure.Get<Guid>("GrantId"));
        Assert.Equal("Rollback", failure.Get<string>("CommandType"));
        Assert.Equal(
            PlayerEquipmentGrantErrorCodes.ConcurrencyConflict,
            failure.Get<string>("ResultCode"));

        var denied = logCollector.FindSingle("grant-log-denied");
        Assert.Equal(LogLevel.Warning, denied.LogLevel);
        Assert.Equal(AdministratorId, denied.Get<Guid>("AdministratorId"));
        Assert.Equal(PlayerId, denied.Get<Guid>("PlayerId"));
        Assert.Equal("GetPage", denied.Get<string>("CommandType"));
        Assert.Equal(
            PlayerEquipmentGrantErrorCodes.Forbidden,
            denied.Get<string>("ResultCode"));

        foreach (var entry in new[] { success, failure, denied })
        {
            AssertSafeLogFields(entry);
            Assert.DoesNotContain(
                "Sensitive success reason",
                entry.RenderedContent,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "Sensitive rollback reason",
                entry.RenderedContent,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                accessToken,
                entry.RenderedContent,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                IdempotencyKey.ToString("D"),
                entry.RenderedContent,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                userAgent,
                entry.RenderedContent,
                StringComparison.Ordinal);
        }
    }

    private static HttpRequestMessage CreateInvalidRequest(
        string invalidCase,
        string bearerToken)
    {
        return invalidCase switch
        {
            "malformed-json" => CreateMutationRequest(
                HttpMethod.Post,
                EquipmentGrantsPath,
                bearerToken,
                ["\"7\""],
                [IdempotencyKey.ToString("D")],
                new StringContent(
                    "{\"configuredItemId\":\"starter-sword\",\"reason\":\"do-not-log-invalid-request-body\"",
                    Encoding.UTF8,
                    "application/json")),
            "empty-json" => CreateMutationRequest(
                HttpMethod.Post,
                EquipmentGrantsPath,
                bearerToken,
                ["\"7\""],
                [IdempotencyKey.ToString("D")],
                new StringContent(string.Empty, Encoding.UTF8, "application/json")),
            "unsupported-media-type" => CreateMutationRequest(
                HttpMethod.Post,
                EquipmentGrantsPath,
                bearerToken,
                ["\"7\""],
                [IdempotencyKey.ToString("D")],
                new StringContent(
                    "do-not-log-invalid-request-body",
                    Encoding.UTF8,
                    "text/plain")),
            "non-integer-page" => CreateRequest(
                HttpMethod.Get,
                $"{EquipmentGrantsPath}?page=not-an-integer",
                bearerToken),
            "malformed-player-id" => CreateRequest(
                HttpMethod.Get,
                "/api/swordmaster/operations/player-assets/not-a-guid/equipment-grants",
                bearerToken),
            "malformed-grant-id" => CreateMutationRequest(
                HttpMethod.Post,
                $"{EquipmentGrantsPath}/not-a-guid/rollback",
                bearerToken,
                ["\"8\""],
                [IdempotencyKey.ToString("D")],
                JsonContent.Create(new RollbackPlayerEquipmentGrantRequest(
                    "do-not-log-invalid-request-body"))),
            _ => throw new ArgumentOutOfRangeException(
                nameof(invalidCase),
                invalidCase,
                null)
        };
    }

    private static string ExpectedCommandType(string invalidCase)
    {
        return invalidCase switch
        {
            "malformed-json" or
            "empty-json" or
            "unsupported-media-type" => "Grant",
            "non-integer-page" or
            "malformed-player-id" => "GetPage",
            "malformed-grant-id" => "Rollback",
            _ => throw new ArgumentOutOfRangeException(
                nameof(invalidCase),
                invalidCase,
                null)
        };
    }

    private static Guid? ExpectedLoggedPlayerId(string invalidCase)
    {
        return invalidCase == "malformed-player-id"
            ? null
            : PlayerId;
    }

    private static Guid? ExpectedLoggedGrantId(string invalidCase)
    {
        return null;
    }

    private static void AssertSafeLogFields(EquipmentGrantLogEntry entry)
    {
        Assert.Equal(
            [
                "AdministratorId",
                "CommandType",
                "CorrelationId",
                "GrantId",
                "PlayerId",
                "ResultCode",
                "Revision",
                "{OriginalFormat}"
            ],
            entry.Properties.Keys.OrderBy(key => key, StringComparer.Ordinal));
    }

    private static void AssertAdministrator(
        EquipmentGrantHttpContextSnapshot context)
    {
        Assert.True(context.IsAdministratorAuthenticated);
        Assert.Equal(AdministratorId, context.AdministratorId);
    }

    private static HttpRequestMessage CreateOperationRequest(
        string operation,
        string? bearerToken)
    {
        return operation switch
        {
            "get" => CreateRequest(HttpMethod.Get, EquipmentGrantsPath, bearerToken),
            "grant" => CreateMutationRequest(
                HttpMethod.Post,
                EquipmentGrantsPath,
                bearerToken,
                ["\"7\""],
                [IdempotencyKey.ToString("D")],
                JsonContent.Create(new GrantPlayerEquipmentRequest(
                    "starter-sword",
                    "Customer support correction"))),
            "rollback" => CreateMutationRequest(
                HttpMethod.Post,
                RollbackPath,
                bearerToken,
                ["\"8\""],
                [IdempotencyKey.ToString("D")],
                JsonContent.Create(new RollbackPlayerEquipmentGrantRequest(
                    "Correction approved by support lead"))),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
        };
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
        HttpContent content)
    {
        var request = CreateRequest(method, path, bearerToken);
        request.Content = content;
        if (ifMatchValues is not null)
        {
            request.Headers.TryAddWithoutValidation(
                IfMatchHeaderName,
                ifMatchValues);
        }
        if (idempotencyValues is not null)
        {
            request.Headers.TryAddWithoutValidation(
                IdempotencyKeyHeaderName,
                idempotencyValues);
        }

        return request;
    }

    private static string CreateAdministratorToken()
    {
        return CreateToken(
            SwordMasterApiFactory.AdminIssuer,
            SwordMasterApiFactory.AdminAudience,
            SwordMasterApiFactory.AdminSigningKey,
            [
                new Claim(SecurityClaimNames.UserId, AdministratorId.ToString("D")),
                new Claim(SecurityClaimNames.UserName, "equipment-grant-administrator")
            ]);
    }

    private static string CreatePlayerToken()
    {
        return CreateToken(
            SwordMasterApiFactory.PlayerIssuer,
            SwordMasterApiFactory.PlayerAudience,
            SwordMasterApiFactory.PlayerSigningKey,
            [
                new Claim(SecurityClaimNames.UserId, PlayerId.ToString("D")),
                new Claim(
                    SwordMasterPlayerAuthentication.TokenUseClaim,
                    SwordMasterPlayerAuthentication.PlayerTokenUse)
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

    private static void AssertStrongEtag(
        HttpResponseMessage response,
        long revision)
    {
        Assert.NotNull(response.Headers.ETag);
        Assert.False(response.Headers.ETag.IsWeak);
        Assert.Equal($"\"{revision}\"", response.Headers.ETag.Tag);
    }

    private static async Task<T> ReadDataAsync<T>(HttpResponseMessage response)
    {
        var envelope = await response.Content.ReadFromJsonAsync<
            NovaCore.Shared.Results.ApiResponse<T>>();
        Assert.NotNull(envelope);
        Assert.True(envelope.Succeeded);
        Assert.Null(envelope.Error);
        return Assert.IsType<T>(envelope.Data);
    }

    private static async Task<string> ReadErrorCodeAsync(
        HttpResponseMessage response)
    {
        return (await ReadErrorAsync(response)).GetProperty("code").GetString()!;
    }

    private static async Task<JsonElement> ReadErrorAsync(
        HttpResponseMessage response)
    {
        var json = await ReadJsonAsync(response);
        Assert.False(json.GetProperty("succeeded").GetBoolean());
        return json.GetProperty("error");
    }

    private static async Task<JsonElement> ReadJsonAsync(
        HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        return document.RootElement.Clone();
    }

    private static string EquipmentGrantsPath =>
        $"/api/swordmaster/operations/player-assets/{PlayerId:D}/equipment-grants";

    private static string RollbackPath =>
        $"{EquipmentGrantsPath}/{GrantId:D}/rollback";

    private sealed class EquipmentGrantLogCollector : ILoggerProvider
    {
        private const string CategoryName =
            "SwordMaster.Api.PlayerAssets.PlayerEquipmentGrantOperationLogger";

        private readonly ConcurrentQueue<EquipmentGrantLogEntry> _entries = new();

        public int Count => _entries.Count;

        public ILogger CreateLogger(string categoryName)
        {
            return new CollectingLogger(categoryName, _entries);
        }

        public EquipmentGrantLogEntry FindSingle(string correlationId)
        {
            return Assert.Single(_entries, entry =>
                string.Equals(entry.CategoryName, CategoryName, StringComparison.Ordinal) &&
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
        ConcurrentQueue<EquipmentGrantLogEntry> entries) : ILogger
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
            if (state is not IEnumerable<KeyValuePair<string, object?>> structuredState)
            {
                return;
            }

            entries.Enqueue(new EquipmentGrantLogEntry(
                categoryName,
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

    private sealed record EquipmentGrantLogEntry(
        string CategoryName,
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

    private sealed class TestCorrelationIdAccessor : ICorrelationIdAccessor
    {
        public string? CorrelationId { get; set; }
    }

    private sealed class TestCurrentAdministrator(Guid administratorId) :
        ICurrentSwordMasterAdministrator
    {
        public bool IsAuthenticated => true;

        public Guid? AdministratorId => administratorId;
    }
}
