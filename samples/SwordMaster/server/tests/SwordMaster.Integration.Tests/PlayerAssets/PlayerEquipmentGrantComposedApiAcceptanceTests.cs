using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MySqlConnector;
using NovaCore.Shared.Results;
using NovaGame.Application.Contracts.Operations.AdminAccess;
using SwordMaster.Application.Contracts.PlayerAssets;
using SwordMaster.Integration.Tests.RuntimeComposition;
using Xunit;

namespace SwordMaster.Integration.Tests.PlayerAssets;

[Collection(SwordMasterRuntimeDatabaseCollection.Name)]
public sealed class PlayerEquipmentGrantComposedApiAcceptanceTests
{
    private const string DisposableDatabaseMarkerVariable =
        "SWORDMASTER_TEST_DISPOSABLE_DATABASE_MARKER";
    private const string DisposableDatabaseMarker = "swordmaster-test-disposable-v1";
    private const string ExpectedNovaGameDatabaseName = "novagame_test";
    private const string ExpectedSwordMasterDatabaseName = "swordmaster_test";
    private const string AdministratorRoleNormalizedName = "ADMINISTRATORS";
    private const string UserAgent = "NovaGame-M306-T02/1.0";
    private const string ConfiguredItemId = "training-sword";
    private const string GrantEvidenceReason = "M306 grant evidence";
    private const string RollbackGrantReason = "M306 rollback grant";
    private const string RollbackReason = "M306 rollback evidence";
    private const long TicksPerMySqlMicrosecond = 10;

    private static readonly Guid UnknownPlayerId =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid EvidenceSeedOwnedItemId =
        Guid.Parse("88888888-8888-8888-8888-888888888888");
    private static readonly Guid RollbackSeedOwnedItemId =
        Guid.Parse("99999999-9999-9999-9999-999999999999");

    private readonly Uri _apiBaseUri;
    private readonly string _novaGameConnectionString;
    private readonly string _swordMasterConnectionString;
    private readonly string _playerToken;
    private readonly Guid _configurationVersionId;
    private readonly Guid _evidencePlayerId;
    private readonly Guid _evidenceCharacterId;
    private readonly Guid _rollbackPlayerId;
    private readonly Guid _rollbackCharacterId;
    private readonly string _grantCorrelationId;
    private readonly string _rollbackCorrelationId;

    public PlayerEquipmentGrantComposedApiAcceptanceTests()
    {
        _apiBaseUri = CreateApiBaseUri(RequireEnvironmentVariable(
            "SWORDMASTER_ACCEPTANCE_COMPOSED_API_URL"));
        _novaGameConnectionString = RequireEnvironmentVariable(
            "SWORDMASTER_TEST_NOVAGAME_MYSQL_CONNECTION_STRING");
        _swordMasterConnectionString = RequireEnvironmentVariable(
            "SWORDMASTER_TEST_MYSQL_CONNECTION_STRING");
        _playerToken = RequireEnvironmentVariable(
            "SWORDMASTER_ACCEPTANCE_COMPOSED_PLAYER_TOKEN");
        _configurationVersionId = RequireGuidEnvironmentVariable(
            "SWORDMASTER_ACCEPTANCE_COMPOSED_CONFIGURATION_VERSION_ID");
        _evidencePlayerId = RequireGuidEnvironmentVariable(
            "SWORDMASTER_ACCEPTANCE_COMPOSED_EVIDENCE_PLAYER_ID");
        _evidenceCharacterId = RequireGuidEnvironmentVariable(
            "SWORDMASTER_ACCEPTANCE_COMPOSED_EVIDENCE_CHARACTER_ID");
        _rollbackPlayerId = RequireGuidEnvironmentVariable(
            "SWORDMASTER_ACCEPTANCE_COMPOSED_ROLLBACK_PLAYER_ID");
        _rollbackCharacterId = RequireGuidEnvironmentVariable(
            "SWORDMASTER_ACCEPTANCE_COMPOSED_ROLLBACK_CHARACTER_ID");
        _grantCorrelationId = RequireEnvironmentVariable(
            "SWORDMASTER_ACCEPTANCE_COMPOSED_GRANT_CORRELATION_ID");
        _rollbackCorrelationId = RequireEnvironmentVariable(
            "SWORDMASTER_ACCEPTANCE_COMPOSED_ROLLBACK_CORRELATION_ID");

        Assert.NotEqual(_evidencePlayerId, _rollbackPlayerId);
        Assert.NotEqual(_evidenceCharacterId, _rollbackCharacterId);
        EnsureDisposableDatabaseTargets();
    }

    [Fact]
    public async Task AdministratorAuthenticationAndPermissionIsolationAreEnforcedBeforeOperationsAsync()
    {
        var grantCountBefore = await CountGlobalGrantsAsync();
        using var anonymousClient = CreateClient();
        using var playerClient = CreateClient();
        using var administratorClient = CreateClient();

        using (var anonymousResponse = await anonymousClient.GetAsync(
                   HistoryPath(UnknownPlayerId)))
        {
            await AssertUnauthorizedAdministratorAsync(anonymousResponse);
        }

        using (var playerRequest = CreateAuthorizedRequest(
                   HttpMethod.Get,
                   HistoryPath(UnknownPlayerId),
                   _playerToken))
        using (var playerResponse = await playerClient.SendAsync(playerRequest))
        {
            await AssertUnauthorizedAdministratorAsync(playerResponse);
        }

        var administratorToken = await AuthenticateAdministratorAsync(administratorClient);
        using (var authorizedLookup = CreateAuthorizedRequest(
                   HttpMethod.Get,
                   HistoryPath(UnknownPlayerId),
                   administratorToken))
        using (var authorizedLookupResponse = await administratorClient.SendAsync(authorizedLookup))
        {
            Assert.Equal(HttpStatusCode.NotFound, authorizedLookupResponse.StatusCode);
            Assert.Equal(
                PlayerEquipmentGrantErrorCodes.PlayerNotFound,
                await ReadErrorCodeAsync(authorizedLookupResponse));
        }

        var permission = await RemoveEquipmentGrantPermissionAsync();
        try
        {
            using (var forbiddenLookup = CreateAuthorizedRequest(
                       HttpMethod.Get,
                       HistoryPath(UnknownPlayerId),
                       administratorToken))
            using (var forbiddenLookupResponse =
                   await administratorClient.SendAsync(forbiddenLookup))
            {
                await AssertForbiddenAsync(forbiddenLookupResponse);
            }

            using (var forbiddenGrant = CreateGrantRequest(
                       UnknownPlayerId,
                       administratorToken,
                       expectedRevision: 1,
                       Guid.NewGuid(),
                       new GrantPlayerEquipmentRequest(
                           ConfiguredItemId,
                           "M306 denied mutation"),
                       correlationId: "m306-permission-denied"))
            using (var forbiddenGrantResponse =
                   await administratorClient.SendAsync(forbiddenGrant))
            {
                await AssertForbiddenAsync(forbiddenGrantResponse);
            }
        }
        finally
        {
            await RestoreEquipmentGrantPermissionAsync(permission);
        }

        Assert.Equal(1, await CountEquipmentGrantPermissionsAsync());
        Assert.Equal(grantCountBefore, await CountGlobalGrantsAsync());
        Assert.Equal(0, await CountGrantsAsync(UnknownPlayerId));
    }

    [Fact]
    public async Task LookupGrantReplayConflictsAndImmutableAuditEvidencePersistThroughRealApiAsync()
    {
        await ResetPlayerAsync(
            _evidencePlayerId,
            _evidenceCharacterId,
            EvidenceSeedOwnedItemId);

        using var administratorClient = CreateClient();
        var administratorToken = await AuthenticateAdministratorAsync(administratorClient);
        var administrator = await GetCurrentAdministratorAsync(
            administratorClient,
            administratorToken);

        var emptyHistory = await GetHistoryAsync(
            administratorClient,
            administratorToken,
            _evidencePlayerId);
        Assert.Equal(1, emptyHistory.Page.Revision);
        Assert.Equal(0, emptyHistory.Page.TotalCount);
        Assert.Empty(emptyHistory.Page.Items);
        Assert.Equal(1, emptyHistory.Revision);

        var grantIdempotencyKey = Guid.NewGuid();
        var grantRequest = new GrantPlayerEquipmentRequest(
            ConfiguredItemId,
            $"  {GrantEvidenceReason}  ");
        var firstGrant = await GrantAsync(
            administratorClient,
            administratorToken,
            _evidencePlayerId,
            expectedRevision: 1,
            grantIdempotencyKey,
            grantRequest,
            _grantCorrelationId);

        Assert.Equal(PlayerEquipmentGrantStatusDto.Active, firstGrant.Result.Status);
        Assert.Equal(_evidencePlayerId, firstGrant.Result.PlayerId);
        Assert.NotEqual(Guid.Empty, firstGrant.Result.GrantId);
        Assert.NotEqual(Guid.Empty, firstGrant.Result.OwnedItemId);
        Assert.Equal(ConfiguredItemId, firstGrant.Result.ConfiguredItemId);
        Assert.Equal(2, firstGrant.Result.ResultingRevision);
        Assert.Equal(administrator.Id, firstGrant.Result.GrantedByAdministratorId);
        Assert.Equal(GrantEvidenceReason, firstGrant.Result.GrantReason);
        Assert.Equal(_configurationVersionId, firstGrant.Result.ConfigurationVersionId);
        Assert.Equal(2, firstGrant.Revision);
        Assert.Equal(DateTimeKind.Utc, firstGrant.Result.GrantedAtUtc.Kind);
        Assert.Null(firstGrant.Result.RolledBackByAdministratorId);
        Assert.Null(firstGrant.Result.RolledBackAtUtc);
        Assert.Null(firstGrant.Result.RollbackReason);

        var replay = await GrantAsync(
            administratorClient,
            administratorToken,
            _evidencePlayerId,
            expectedRevision: 1,
            grantIdempotencyKey,
            grantRequest,
            _grantCorrelationId);
        Assert.Equal(firstGrant.Result, replay.Result);
        Assert.Equal(2, replay.Revision);

        using (var changedFingerprintRequest = CreateGrantRequest(
                   _evidencePlayerId,
                   administratorToken,
                   expectedRevision: 1,
                   grantIdempotencyKey,
                   new GrantPlayerEquipmentRequest(
                       ConfiguredItemId,
                       "M306 changed grant evidence"),
                   _grantCorrelationId))
        using (var changedFingerprintResponse =
               await administratorClient.SendAsync(changedFingerprintRequest))
        {
            Assert.Equal(HttpStatusCode.Conflict, changedFingerprintResponse.StatusCode);
            Assert.Equal(
                PlayerEquipmentGrantErrorCodes.IdempotencyConflict,
                await ReadErrorCodeAsync(changedFingerprintResponse));
        }

        using (var staleRevisionRequest = CreateGrantRequest(
                   _evidencePlayerId,
                   administratorToken,
                   expectedRevision: 1,
                   Guid.NewGuid(),
                   new GrantPlayerEquipmentRequest(
                       ConfiguredItemId,
                       "M306 stale grant evidence"),
                   _grantCorrelationId))
        using (var staleRevisionResponse =
               await administratorClient.SendAsync(staleRevisionRequest))
        {
            Assert.Equal(HttpStatusCode.Conflict, staleRevisionResponse.StatusCode);
            Assert.Equal(
                PlayerEquipmentGrantErrorCodes.ConcurrencyConflict,
                await ReadErrorCodeAsync(staleRevisionResponse));
        }

        var persisted = Require(await FindGrantAsync(
            _evidencePlayerId,
            firstGrant.Result.GrantId));
        AssertEvidenceGrant(
            persisted,
            firstGrant.Result,
            administrator.Id,
            grantIdempotencyKey,
            _grantCorrelationId);
        var state = await ReadPlayerStateAsync(
            _evidencePlayerId,
            firstGrant.Result.OwnedItemId);
        Assert.Equal(2, state.Revision);
        Assert.Equal(1, state.GrantCount);
        Assert.Equal(1, state.ActiveGrantCount);
        Assert.Equal(0, state.RolledBackGrantCount);
        Assert.Equal(1, state.IssuedOwnedItemCount);
        Assert.Equal(0, state.AssignmentCount);
        Assert.Equal(0, state.ReceiptCount);

        var reloadedHistory = await GetHistoryAsync(
            administratorClient,
            administratorToken,
            _evidencePlayerId);
        Assert.Equal(2, reloadedHistory.Page.Revision);
        Assert.Equal(_evidencePlayerId, reloadedHistory.Page.PlayerId);
        Assert.Equal(1, reloadedHistory.Page.Page);
        Assert.Equal(20, reloadedHistory.Page.PageSize);
        Assert.Equal(1, reloadedHistory.Page.TotalCount);
        var historyGrant = Assert.Single(reloadedHistory.Page.Items);
        Assert.Equal(firstGrant.Result.GrantId, historyGrant.GrantId);
        Assert.Equal(_evidencePlayerId, historyGrant.PlayerId);
        Assert.Equal(firstGrant.Result.OwnedItemId, historyGrant.OwnedItemId);
        Assert.Equal(ConfiguredItemId, historyGrant.ConfiguredItemId);
        Assert.Equal(_configurationVersionId, historyGrant.ConfigurationVersionId);
        Assert.Equal(PlayerEquipmentGrantStatusDto.Active, historyGrant.Status);
        Assert.Equal(2, historyGrant.GrantResultingRevision);
        Assert.Equal(administrator.Id, historyGrant.GrantedByAdministratorId);
        AssertUtcDateTimeEqualAtMySqlPrecision(
            firstGrant.Result.GrantedAtUtc,
            historyGrant.GrantedAtUtc);
        Assert.Equal(GrantEvidenceReason, historyGrant.GrantReason);
        Assert.Null(historyGrant.RollbackResultingRevision);
        Assert.Null(historyGrant.RolledBackByAdministratorId);
        Assert.Null(historyGrant.RolledBackAtUtc);
        Assert.Null(historyGrant.RollbackReason);
    }

    [Fact]
    public async Task ConcurrentGrantThenPlayerEquipRollbackAndReloadRemainDurableAsync()
    {
        await ResetPlayerAsync(
            _rollbackPlayerId,
            _rollbackCharacterId,
            RollbackSeedOwnedItemId);

        using var administratorClient = CreateClient();
        var administratorToken = await AuthenticateAdministratorAsync(administratorClient);
        var administrator = await GetCurrentAdministratorAsync(
            administratorClient,
            administratorToken);
        var grantIdempotencyKey = Guid.NewGuid();
        var grantRequest = new GrantPlayerEquipmentRequest(
            ConfiguredItemId,
            $"  {RollbackGrantReason}  ");

        var concurrentGrants = await GrantConcurrentlyAsync(
            administratorToken,
            _rollbackPlayerId,
            expectedRevision: 1,
            grantIdempotencyKey,
            grantRequest,
            _grantCorrelationId);
        Assert.Equal(concurrentGrants.First.Result, concurrentGrants.Second.Result);
        Assert.Equal(PlayerEquipmentGrantStatusDto.Active, concurrentGrants.First.Result.Status);
        Assert.Equal(2, concurrentGrants.First.Result.ResultingRevision);
        Assert.Equal(2, concurrentGrants.First.Revision);
        Assert.Equal(2, concurrentGrants.Second.Revision);

        var grant = concurrentGrants.First.Result;
        var afterConcurrentGrant = await ReadPlayerStateAsync(
            _rollbackPlayerId,
            grant.OwnedItemId);
        Assert.Equal(2, afterConcurrentGrant.Revision);
        Assert.Equal(1, afterConcurrentGrant.GrantCount);
        Assert.Equal(1, afterConcurrentGrant.ActiveGrantCount);
        Assert.Equal(1, afterConcurrentGrant.IssuedOwnedItemCount);
        Assert.Equal(0, afterConcurrentGrant.AssignmentCount);

        using var playerClient = CreateClient();
        using (var equipRequest = CreatePlayerMutationRequest(
                   HttpMethod.Put,
                   PlayerEquipmentPath(_rollbackCharacterId),
                   expectedRevision: 2,
                   Guid.NewGuid(),
                   JsonContent.Create(new EquipEquipmentRequest(grant.OwnedItemId))))
        using (var equipResponse = await playerClient.SendAsync(
                   AddAuthorization(equipRequest, _playerToken)))
        {
            var equip = await ReadPlayerMutationAsync(equipResponse, expectedRevision: 3);
            Assert.Equal(3, equip.Revision);
        }

        var blockedRollbackKey = Guid.NewGuid();
        using (var blockedRollbackRequest = CreateRollbackRequest(
                   _rollbackPlayerId,
                   grant.GrantId,
                   administratorToken,
                   expectedRevision: 3,
                   blockedRollbackKey,
                   new RollbackPlayerEquipmentGrantRequest(RollbackReason),
                   _rollbackCorrelationId))
        using (var blockedRollbackResponse =
               await administratorClient.SendAsync(blockedRollbackRequest))
        {
            Assert.Equal(HttpStatusCode.Conflict, blockedRollbackResponse.StatusCode);
            Assert.Equal(
                PlayerEquipmentGrantErrorCodes.RollbackBlockedItemEquipped,
                await ReadErrorCodeAsync(blockedRollbackResponse));
        }

        var blockedState = await ReadPlayerStateAsync(
            _rollbackPlayerId,
            grant.OwnedItemId);
        Assert.Equal(3, blockedState.Revision);
        Assert.Equal(1, blockedState.GrantCount);
        Assert.Equal(1, blockedState.ActiveGrantCount);
        Assert.Equal(1, blockedState.IssuedOwnedItemCount);
        Assert.Equal(1, blockedState.AssignmentCount);
        Assert.Equal(1, blockedState.ReceiptCount);

        using (var unequipRequest = CreatePlayerMutationRequest(
                   HttpMethod.Delete,
                   PlayerEquipmentPath(_rollbackCharacterId),
                   expectedRevision: 3,
                   Guid.NewGuid()))
        using (var unequipResponse = await playerClient.SendAsync(
                   AddAuthorization(unequipRequest, _playerToken)))
        {
            var unequip = await ReadPlayerMutationAsync(unequipResponse, expectedRevision: 4);
            Assert.Equal(4, unequip.Revision);
        }

        var rollbackIdempotencyKey = Guid.NewGuid();
        var rollbackRequest = new RollbackPlayerEquipmentGrantRequest(
            $"  {RollbackReason}  ");
        var rollback = await RollbackAsync(
            administratorClient,
            administratorToken,
            _rollbackPlayerId,
            grant.GrantId,
            expectedRevision: 4,
            rollbackIdempotencyKey,
            rollbackRequest,
            _rollbackCorrelationId);
        Assert.Equal(PlayerEquipmentGrantStatusDto.RolledBack, rollback.Result.Status);
        Assert.Equal(_rollbackPlayerId, rollback.Result.PlayerId);
        Assert.Equal(grant.OwnedItemId, rollback.Result.OwnedItemId);
        Assert.Equal(ConfiguredItemId, rollback.Result.ConfiguredItemId);
        Assert.Equal(_configurationVersionId, rollback.Result.ConfigurationVersionId);
        Assert.Equal(5, rollback.Result.ResultingRevision);
        Assert.Equal(administrator.Id, rollback.Result.GrantedByAdministratorId);
        AssertUtcDateTimeEqualAtMySqlPrecision(
            grant.GrantedAtUtc,
            rollback.Result.GrantedAtUtc);
        Assert.Equal(RollbackGrantReason, rollback.Result.GrantReason);
        Assert.Equal(administrator.Id, rollback.Result.RolledBackByAdministratorId);
        Assert.Equal(5, rollback.Revision);
        Assert.Equal(RollbackReason, rollback.Result.RollbackReason);
        Assert.NotNull(rollback.Result.RolledBackAtUtc);
        Assert.Equal(DateTimeKind.Utc, rollback.Result.RolledBackAtUtc!.Value.Kind);

        var rollbackReplay = await RollbackAsync(
            administratorClient,
            administratorToken,
            _rollbackPlayerId,
            grant.GrantId,
            expectedRevision: 4,
            rollbackIdempotencyKey,
            rollbackRequest,
            _rollbackCorrelationId);
        Assert.Equal(rollback.Result, rollbackReplay.Result);
        Assert.Equal(5, rollbackReplay.Revision);

        using var freshAdministratorClient = CreateClient();
        var history = await GetHistoryAsync(
            freshAdministratorClient,
            administratorToken,
            _rollbackPlayerId);
        Assert.Equal(5, history.Page.Revision);
        Assert.Equal(_rollbackPlayerId, history.Page.PlayerId);
        Assert.Equal(1, history.Page.Page);
        Assert.Equal(20, history.Page.PageSize);
        Assert.Equal(1, history.Page.TotalCount);
        var historyGrant = Assert.Single(history.Page.Items);
        Assert.Equal(PlayerEquipmentGrantStatusDto.RolledBack, historyGrant.Status);
        Assert.Equal(grant.GrantId, historyGrant.GrantId);
        Assert.Equal(_rollbackPlayerId, historyGrant.PlayerId);
        Assert.Equal(grant.OwnedItemId, historyGrant.OwnedItemId);
        Assert.Equal(ConfiguredItemId, historyGrant.ConfiguredItemId);
        Assert.Equal(_configurationVersionId, historyGrant.ConfigurationVersionId);
        Assert.Equal(2, historyGrant.GrantResultingRevision);
        Assert.Equal(administrator.Id, historyGrant.GrantedByAdministratorId);
        AssertUtcDateTimeEqualAtMySqlPrecision(grant.GrantedAtUtc, historyGrant.GrantedAtUtc);
        Assert.Equal(RollbackGrantReason, historyGrant.GrantReason);
        Assert.Equal(5, historyGrant.RollbackResultingRevision);
        Assert.Equal(administrator.Id, historyGrant.RolledBackByAdministratorId);
        Assert.NotNull(historyGrant.RolledBackAtUtc);
        AssertUtcDateTimeEqualAtMySqlPrecision(
            rollback.Result.RolledBackAtUtc!.Value,
            historyGrant.RolledBackAtUtc!.Value);
        Assert.Equal(RollbackReason, historyGrant.RollbackReason);

        var persisted = Require(await FindGrantAsync(_rollbackPlayerId, grant.GrantId));
        AssertRollbackGrant(
            persisted,
            grant,
            rollback.Result,
            administrator.Id,
            grantIdempotencyKey,
            rollbackIdempotencyKey,
            _grantCorrelationId,
            _rollbackCorrelationId);
        var finalState = await ReadPlayerStateAsync(_rollbackPlayerId, grant.OwnedItemId);
        Assert.Equal(5, finalState.Revision);
        Assert.Equal(1, finalState.GrantCount);
        Assert.Equal(0, finalState.ActiveGrantCount);
        Assert.Equal(1, finalState.RolledBackGrantCount);
        Assert.Equal(0, finalState.IssuedOwnedItemCount);
        Assert.Equal(0, finalState.AssignmentCount);
        Assert.Equal(2, finalState.ReceiptCount);
        Assert.Equal(new long[] { 3, 4 }, await ReadReceiptRevisionsAsync(_rollbackPlayerId));
    }

    private async Task<(MutationResponse First, MutationResponse Second)> GrantConcurrentlyAsync(
        string administratorToken,
        Guid playerId,
        long expectedRevision,
        Guid idempotencyKey,
        GrantPlayerEquipmentRequest request,
        string correlationId)
    {
        using var firstClient = CreateClient();
        using var secondClient = CreateClient();
        using var firstRequest = CreateGrantRequest(
            playerId,
            administratorToken,
            expectedRevision,
            idempotencyKey,
            request,
            correlationId);
        using var secondRequest = CreateGrantRequest(
            playerId,
            administratorToken,
            expectedRevision,
            idempotencyKey,
            request,
            correlationId);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var requests = new[]
        {
            firstClient.SendAsync(firstRequest, timeout.Token),
            secondClient.SendAsync(secondRequest, timeout.Token)
        };
        HttpResponseMessage[] responses;
        try
        {
            responses = await Task.WhenAll(requests).WaitAsync(timeout.Token);
        }
        catch
        {
            timeout.Cancel();
            await DrainResponsesAsync(requests);
            throw;
        }

        try
        {
            var first = await ReadMutationResponseAsync(responses[0], expectedRevision: 2);
            var second = await ReadMutationResponseAsync(responses[1], expectedRevision: 2);
            return (first, second);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    private async Task<MutationResponse> GrantAsync(
        HttpClient client,
        string administratorToken,
        Guid playerId,
        long expectedRevision,
        Guid idempotencyKey,
        GrantPlayerEquipmentRequest request,
        string correlationId)
    {
        using var httpRequest = CreateGrantRequest(
            playerId,
            administratorToken,
            expectedRevision,
            idempotencyKey,
            request,
            correlationId);
        using var response = await client.SendAsync(httpRequest);
        return await ReadMutationResponseAsync(response, expectedRevision: 2);
    }

    private async Task<MutationResponse> RollbackAsync(
        HttpClient client,
        string administratorToken,
        Guid playerId,
        Guid grantId,
        long expectedRevision,
        Guid idempotencyKey,
        RollbackPlayerEquipmentGrantRequest request,
        string correlationId)
    {
        using var httpRequest = CreateRollbackRequest(
            playerId,
            grantId,
            administratorToken,
            expectedRevision,
            idempotencyKey,
            request,
            correlationId);
        using var response = await client.SendAsync(httpRequest);
        return await ReadMutationResponseAsync(response, expectedRevision: 5);
    }

    private async Task<HistoryResponse> GetHistoryAsync(
        HttpClient client,
        string administratorToken,
        Guid playerId)
    {
        using var request = CreateAuthorizedRequest(
            HttpMethod.Get,
            HistoryPath(playerId),
            administratorToken);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await ReadDataAsync<PlayerEquipmentGrantPageDto>(response);
        var revision = AssertStrongEtag(response, page.Revision);
        return new HistoryResponse(page, revision);
    }

    private async Task<string> AuthenticateAdministratorAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest
            {
                UserName = RequireEnvironmentVariable(
                    "NovaGame__AdminAccess__Seed__UserName"),
                Password = RequireEnvironmentVariable(
                    "NovaGame__AdminAccess__Seed__Password")
            });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var accessToken = (await ReadDataAsync<AccessTokenResponse>(response)).AccessToken;
        Assert.False(string.IsNullOrWhiteSpace(accessToken));
        return accessToken;
    }

    private async Task<AdministratorInfoDto> GetCurrentAdministratorAsync(
        HttpClient client,
        string administratorToken)
    {
        using var request = CreateAuthorizedRequest(
            HttpMethod.Get,
            "/api/user/info",
            administratorToken);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var administrator = await ReadDataAsync<AdministratorInfoDto>(response);
        Assert.NotEqual(Guid.Empty, administrator.Id);
        Assert.Contains(
            PlayerEquipmentGrantPermissions.Manage,
            administrator.PermissionCodes);
        return administrator;
    }

    private HttpClient CreateClient()
    {
        return new HttpClient
        {
            BaseAddress = _apiBaseUri,
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    private static HttpRequestMessage CreateAuthorizedRequest(
        HttpMethod method,
        string path,
        string accessToken)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        return request;
    }

    private static HttpRequestMessage CreateGrantRequest(
        Guid playerId,
        string administratorToken,
        long expectedRevision,
        Guid idempotencyKey,
        GrantPlayerEquipmentRequest request,
        string correlationId)
    {
        var httpRequest = CreateAuthorizedRequest(
            HttpMethod.Post,
            CollectionPath(playerId),
            administratorToken);
        AddMutationHeaders(httpRequest, expectedRevision, idempotencyKey, correlationId);
        httpRequest.Content = JsonContent.Create(request);
        return httpRequest;
    }

    private static HttpRequestMessage CreateRollbackRequest(
        Guid playerId,
        Guid grantId,
        string administratorToken,
        long expectedRevision,
        Guid idempotencyKey,
        RollbackPlayerEquipmentGrantRequest request,
        string correlationId)
    {
        var httpRequest = CreateAuthorizedRequest(
            HttpMethod.Post,
            $"{CollectionPath(playerId)}/{grantId:D}/rollback",
            administratorToken);
        AddMutationHeaders(httpRequest, expectedRevision, idempotencyKey, correlationId);
        httpRequest.Content = JsonContent.Create(request);
        return httpRequest;
    }

    private static HttpRequestMessage CreatePlayerMutationRequest(
        HttpMethod method,
        string path,
        long expectedRevision,
        Guid idempotencyKey,
        HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = content
        };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{expectedRevision}\"");
        request.Headers.TryAddWithoutValidation(
            "Idempotency-Key",
            idempotencyKey.ToString("D"));
        request.Headers.UserAgent.ParseAdd(UserAgent);
        return request;
    }

    private static HttpRequestMessage AddAuthorization(
        HttpRequestMessage request,
        string accessToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private static void AddMutationHeaders(
        HttpRequestMessage request,
        long expectedRevision,
        Guid idempotencyKey,
        string correlationId)
    {
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{expectedRevision}\"");
        request.Headers.TryAddWithoutValidation(
            "Idempotency-Key",
            idempotencyKey.ToString("D"));
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);
    }

    private static async Task<MutationResponse> ReadMutationResponseAsync(
        HttpResponseMessage response,
        long expectedRevision)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await ReadDataAsync<PlayerEquipmentGrantMutationResultDto>(response);
        Assert.Equal(expectedRevision, result.ResultingRevision);
        var revision = AssertStrongEtag(response, expectedRevision);
        return new MutationResponse(result, revision);
    }

    private static async Task<PlayerAssetsMutationResultDto> ReadPlayerMutationAsync(
        HttpResponseMessage response,
        long expectedRevision)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await ReadDataAsync<PlayerAssetsMutationResultDto>(response);
        Assert.Equal(expectedRevision, result.Revision);
        AssertStrongEtag(response, expectedRevision);
        return result;
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

    private static async Task AssertUnauthorizedAdministratorAsync(
        HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(
            PlayerEquipmentGrantErrorCodes.UnauthorizedAdministrator,
            await ReadErrorCodeAsync(response));
        Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
    }

    private static async Task AssertForbiddenAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(
            PlayerEquipmentGrantErrorCodes.Forbidden,
            await ReadErrorCodeAsync(response));
    }

    private static long AssertStrongEtag(HttpResponseMessage response, long expectedRevision)
    {
        Assert.NotNull(response.Headers.ETag);
        Assert.False(response.Headers.ETag.IsWeak);
        Assert.Equal($"\"{expectedRevision}\"", response.Headers.ETag.Tag);
        return expectedRevision;
    }

    private static void AssertUtcDateTimeEqualAtMySqlPrecision(
        DateTime expectedUtc,
        DateTime persistedValue)
    {
        Assert.Equal(DateTimeKind.Utc, expectedUtc.Kind);
        Assert.Equal(
            NormalizeToMySqlMicrosecondTicks(expectedUtc),
            NormalizeToMySqlMicrosecondTicks(persistedValue));
    }

    private static long NormalizeToMySqlMicrosecondTicks(DateTime value)
    {
        return value.Ticks - (value.Ticks % TicksPerMySqlMicrosecond);
    }

    private async Task ResetPlayerAsync(
        Guid playerId,
        Guid characterId,
        Guid ownedItemId)
    {
        await using var connection = new MySqlConnection(_swordMasterConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        foreach (var commandText in new[]
        {
            "DELETE FROM sm_player_equipment_grants WHERE PlayerId = @playerId",
            "DELETE FROM sm_player_asset_command_receipts WHERE PlayerId = @playerId",
            "DELETE FROM sm_equipment_assignments WHERE PlayerId = @playerId",
            "DELETE FROM sm_owned_equipment_items WHERE PlayerId = @playerId",
            "DELETE FROM sm_player_character_base_attributes WHERE PlayerId = @playerId",
            "DELETE FROM sm_player_characters WHERE PlayerId = @playerId",
            "DELETE FROM sm_player_assets_profiles WHERE PlayerId = @playerId"
        })
        {
            await ExecuteNonQueryAsync(
                connection,
                transaction,
                commandText,
                ("@playerId", playerId));
        }

        await ExecuteNonQueryAsync(
            connection,
            transaction,
            "INSERT INTO sm_player_assets_profiles (PlayerId, Revision) VALUES (@playerId, 1)",
            ("@playerId", playerId));
        await ExecuteNonQueryAsync(
            connection,
            transaction,
            "INSERT INTO sm_player_characters (PlayerId, CharacterId) VALUES (@playerId, @characterId)",
            ("@playerId", playerId),
            ("@characterId", characterId));
        await ExecuteNonQueryAsync(
            connection,
            transaction,
            "INSERT INTO sm_player_character_base_attributes (PlayerId, StatCode, StatValue) VALUES (@playerId, 'attack', 10), (@playerId, 'health', 100)",
            ("@playerId", playerId));
        await ExecuteNonQueryAsync(
            connection,
            transaction,
            "INSERT INTO sm_owned_equipment_items (PlayerId, OwnedItemId, ConfiguredItemId) VALUES (@playerId, @ownedItemId, @configuredItemId)",
            ("@playerId", playerId),
            ("@ownedItemId", ownedItemId),
            ("@configuredItemId", ConfiguredItemId));
        await transaction.CommitAsync();
    }

    private async Task<PersistedRolePermission> RemoveEquipmentGrantPermissionAsync()
    {
        await using var connection = new MySqlConnection(_novaGameConnectionString);
        await connection.OpenAsync();
        var permission = Require(await ReadEquipmentGrantPermissionAsync(connection));
        await using var command = CreateCommand(
            connection,
            transaction: null,
            "DELETE FROM admin_role_permissions WHERE Id = @id",
            ("@id", permission.Id));
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
        return permission;
    }

    private async Task RestoreEquipmentGrantPermissionAsync(
        PersistedRolePermission permission)
    {
        await using var connection = new MySqlConnection(_novaGameConnectionString);
        await connection.OpenAsync();
        await using var command = CreateCommand(
            connection,
            transaction: null,
            "INSERT INTO admin_role_permissions (Id, RoleId, Name, GrantedAtUtc, GrantedByAdministratorId) VALUES (@id, @roleId, @name, @grantedAtUtc, @grantedByAdministratorId)",
            ("@id", permission.Id),
            ("@roleId", permission.RoleId),
            ("@name", permission.Name),
            ("@grantedAtUtc", permission.GrantedAtUtc),
            ("@grantedByAdministratorId", permission.GrantedByAdministratorId));
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private async Task<PersistedRolePermission?> ReadEquipmentGrantPermissionAsync(
        MySqlConnection connection)
    {
        await using var command = CreateCommand(
            connection,
            transaction: null,
            """
            SELECT permission.Id, permission.RoleId, permission.Name, permission.GrantedAtUtc, permission.GrantedByAdministratorId
            FROM admin_role_permissions AS permission
            INNER JOIN admin_roles AS role ON role.Id = permission.RoleId
            WHERE role.NormalizedName = @roleName AND permission.Name = @permissionName
            """,
            ("@roleName", AdministratorRoleNormalizedName),
            ("@permissionName", PlayerEquipmentGrantPermissions.Manage));
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        var permission = new PersistedRolePermission(
            ReadRequiredGuid(reader, 0),
            ReadRequiredGuid(reader, 1),
            reader.GetString(2),
            reader.GetDateTime(3),
            GetNullableGuid(reader, 4));
        Assert.False(await reader.ReadAsync());
        return permission;
    }

    private async Task<long> CountEquipmentGrantPermissionsAsync()
    {
        await using var connection = new MySqlConnection(_novaGameConnectionString);
        await connection.OpenAsync();
        return await CountAsync(
            connection,
            """
            SELECT COUNT(*)
            FROM admin_role_permissions AS permission
            INNER JOIN admin_roles AS role ON role.Id = permission.RoleId
            WHERE role.NormalizedName = @roleName AND permission.Name = @permissionName
            """,
            ("@roleName", AdministratorRoleNormalizedName),
            ("@permissionName", PlayerEquipmentGrantPermissions.Manage));
    }

    private async Task<long> CountGlobalGrantsAsync()
    {
        await using var connection = new MySqlConnection(_swordMasterConnectionString);
        await connection.OpenAsync();
        return await CountAsync(connection, "SELECT COUNT(*) FROM sm_player_equipment_grants");
    }

    private async Task<long> CountGrantsAsync(Guid playerId)
    {
        await using var connection = new MySqlConnection(_swordMasterConnectionString);
        await connection.OpenAsync();
        return await CountAsync(
            connection,
            "SELECT COUNT(*) FROM sm_player_equipment_grants WHERE PlayerId = @playerId",
            ("@playerId", playerId));
    }

    private async Task<PlayerState> ReadPlayerStateAsync(
        Guid playerId,
        Guid issuedOwnedItemId)
    {
        await using var connection = new MySqlConnection(_swordMasterConnectionString);
        await connection.OpenAsync();
        var revision = await ReadNullableLongAsync(
            connection,
            "SELECT Revision FROM sm_player_assets_profiles WHERE PlayerId = @playerId",
            ("@playerId", playerId));
        var grantCount = await CountAsync(
            connection,
            "SELECT COUNT(*) FROM sm_player_equipment_grants WHERE PlayerId = @playerId",
            ("@playerId", playerId));
        var activeGrantCount = await CountAsync(
            connection,
            "SELECT COUNT(*) FROM sm_player_equipment_grants WHERE PlayerId = @playerId AND Status = 1",
            ("@playerId", playerId));
        var rolledBackGrantCount = await CountAsync(
            connection,
            "SELECT COUNT(*) FROM sm_player_equipment_grants WHERE PlayerId = @playerId AND Status = 2",
            ("@playerId", playerId));
        var issuedOwnedItemCount = await CountAsync(
            connection,
            "SELECT COUNT(*) FROM sm_owned_equipment_items WHERE PlayerId = @playerId AND OwnedItemId = @ownedItemId",
            ("@playerId", playerId),
            ("@ownedItemId", issuedOwnedItemId));
        var assignmentCount = await CountAsync(
            connection,
            "SELECT COUNT(*) FROM sm_equipment_assignments WHERE PlayerId = @playerId",
            ("@playerId", playerId));
        var receiptCount = await CountAsync(
            connection,
            "SELECT COUNT(*) FROM sm_player_asset_command_receipts WHERE PlayerId = @playerId",
            ("@playerId", playerId));
        return new PlayerState(
            revision,
            grantCount,
            activeGrantCount,
            rolledBackGrantCount,
            issuedOwnedItemCount,
            assignmentCount,
            receiptCount);
    }

    private async Task<IReadOnlyList<long>> ReadReceiptRevisionsAsync(Guid playerId)
    {
        await using var connection = new MySqlConnection(_swordMasterConnectionString);
        await connection.OpenAsync();
        await using var command = CreateCommand(
            connection,
            transaction: null,
            "SELECT ResultingRevision FROM sm_player_asset_command_receipts WHERE PlayerId = @playerId ORDER BY ResultingRevision",
            ("@playerId", playerId));
        await using var reader = await command.ExecuteReaderAsync();
        var revisions = new List<long>();
        while (await reader.ReadAsync())
        {
            revisions.Add(reader.GetInt64(0));
        }

        return revisions;
    }

    private async Task<PersistedGrant?> FindGrantAsync(Guid playerId, Guid grantId)
    {
        await using var connection = new MySqlConnection(_swordMasterConnectionString);
        await connection.OpenAsync();
        await using var command = CreateCommand(
            connection,
            transaction: null,
            """
            SELECT GrantId, PlayerId, OwnedItemId, ConfiguredItemId, ConfigurationVersionId, Status,
                   GrantedByAdministratorId, GrantedAtUtc, GrantReason, GrantIdempotencyKey,
                   GrantRequestFingerprint, GrantResultingRevision, GrantIpAddress, GrantUserAgent,
                   GrantCorrelationId, RolledBackByAdministratorId, RolledBackAtUtc, RollbackReason,
                   RollbackIdempotencyKey, RollbackRequestFingerprint, RollbackResultingRevision,
                   RollbackIpAddress, RollbackUserAgent, RollbackCorrelationId
            FROM sm_player_equipment_grants
            WHERE PlayerId = @playerId AND GrantId = @grantId
            """,
            ("@playerId", playerId),
            ("@grantId", grantId));
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        var grant = new PersistedGrant(
            ReadRequiredGuid(reader, 0),
            ReadRequiredGuid(reader, 1),
            ReadRequiredGuid(reader, 2),
            reader.GetString(3),
            ReadRequiredGuid(reader, 4),
            reader.GetByte(5),
            ReadRequiredGuid(reader, 6),
            reader.GetDateTime(7),
            reader.GetString(8),
            ReadRequiredGuid(reader, 9),
            reader.GetString(10),
            reader.GetInt64(11),
            GetNullableString(reader, 12),
            GetNullableString(reader, 13),
            GetNullableString(reader, 14),
            GetNullableGuid(reader, 15),
            GetNullableDateTime(reader, 16),
            GetNullableString(reader, 17),
            GetNullableGuid(reader, 18),
            GetNullableString(reader, 19),
            GetNullableLong(reader, 20),
            GetNullableString(reader, 21),
            GetNullableString(reader, 22),
            GetNullableString(reader, 23));
        Assert.False(await reader.ReadAsync());
        return grant;
    }

    private void AssertEvidenceGrant(
        PersistedGrant persisted,
        PlayerEquipmentGrantMutationResultDto result,
        Guid administratorId,
        Guid idempotencyKey,
        string correlationId)
    {
        Assert.Equal(result.GrantId, persisted.GrantId);
        Assert.Equal(_evidencePlayerId, persisted.PlayerId);
        Assert.Equal(result.OwnedItemId, persisted.OwnedItemId);
        Assert.Equal(ConfiguredItemId, persisted.ConfiguredItemId);
        Assert.Equal(_configurationVersionId, persisted.ConfigurationVersionId);
        Assert.Equal((byte)PlayerEquipmentGrantStatusDto.Active, persisted.Status);
        Assert.Equal(administratorId, persisted.GrantedByAdministratorId);
        Assert.Equal(GrantEvidenceReason, persisted.GrantReason);
        Assert.Equal(idempotencyKey, persisted.GrantIdempotencyKey);
        Assert.Equal(
            CreateExpectedGrantFingerprint(
                _evidencePlayerId,
                expectedRevision: 1,
                ConfiguredItemId,
                GrantEvidenceReason),
            persisted.GrantRequestFingerprint);
        Assert.Matches("^[A-F0-9]{64}$", persisted.GrantRequestFingerprint);
        Assert.Equal(2, persisted.GrantResultingRevision);
        Assert.Equal(correlationId, persisted.GrantCorrelationId);
        Assert.Equal(UserAgent, persisted.GrantUserAgent);
        Assert.False(string.IsNullOrWhiteSpace(persisted.GrantIpAddress));
        AssertUtcDateTimeEqualAtMySqlPrecision(result.GrantedAtUtc, persisted.GrantedAtUtc);
        Assert.Null(persisted.RolledBackByAdministratorId);
        Assert.Null(persisted.RolledBackAtUtc);
        Assert.Null(persisted.RollbackReason);
        Assert.Null(persisted.RollbackIdempotencyKey);
        Assert.Null(persisted.RollbackRequestFingerprint);
        Assert.Null(persisted.RollbackResultingRevision);
        Assert.Null(persisted.RollbackIpAddress);
        Assert.Null(persisted.RollbackUserAgent);
        Assert.Null(persisted.RollbackCorrelationId);
    }

    private void AssertRollbackGrant(
        PersistedGrant persisted,
        PlayerEquipmentGrantMutationResultDto grant,
        PlayerEquipmentGrantMutationResultDto rollback,
        Guid administratorId,
        Guid grantIdempotencyKey,
        Guid rollbackIdempotencyKey,
        string grantCorrelationId,
        string rollbackCorrelationId)
    {
        Assert.Equal(grant.GrantId, persisted.GrantId);
        Assert.Equal(_rollbackPlayerId, persisted.PlayerId);
        Assert.Equal(grant.OwnedItemId, persisted.OwnedItemId);
        Assert.Equal(ConfiguredItemId, persisted.ConfiguredItemId);
        Assert.Equal(_configurationVersionId, persisted.ConfigurationVersionId);
        Assert.Equal((byte)PlayerEquipmentGrantStatusDto.RolledBack, persisted.Status);
        Assert.Equal(administratorId, persisted.GrantedByAdministratorId);
        Assert.Equal(RollbackGrantReason, persisted.GrantReason);
        Assert.Equal(grantIdempotencyKey, persisted.GrantIdempotencyKey);
        Assert.Equal(
            CreateExpectedGrantFingerprint(
                _rollbackPlayerId,
                expectedRevision: 1,
                ConfiguredItemId,
                RollbackGrantReason),
            persisted.GrantRequestFingerprint);
        Assert.Matches("^[A-F0-9]{64}$", persisted.GrantRequestFingerprint);
        Assert.Equal(2, persisted.GrantResultingRevision);
        Assert.Equal(grantCorrelationId, persisted.GrantCorrelationId);
        Assert.Equal(UserAgent, persisted.GrantUserAgent);
        Assert.False(string.IsNullOrWhiteSpace(persisted.GrantIpAddress));
        AssertUtcDateTimeEqualAtMySqlPrecision(grant.GrantedAtUtc, persisted.GrantedAtUtc);

        Assert.Equal(administratorId, persisted.RolledBackByAdministratorId);
        Assert.NotNull(persisted.RolledBackAtUtc);
        Assert.Equal(RollbackReason, persisted.RollbackReason);
        Assert.Equal(rollbackIdempotencyKey, persisted.RollbackIdempotencyKey);
        Assert.Equal(
            CreateExpectedRollbackFingerprint(
                _rollbackPlayerId,
                grant.GrantId,
                expectedRevision: 4,
                RollbackReason),
            persisted.RollbackRequestFingerprint);
        Assert.Matches("^[A-F0-9]{64}$", persisted.RollbackRequestFingerprint!);
        Assert.Equal(5, persisted.RollbackResultingRevision);
        Assert.Equal(rollbackCorrelationId, persisted.RollbackCorrelationId);
        Assert.Equal(UserAgent, persisted.RollbackUserAgent);
        Assert.False(string.IsNullOrWhiteSpace(persisted.RollbackIpAddress));
        AssertUtcDateTimeEqualAtMySqlPrecision(
            rollback.RolledBackAtUtc!.Value,
            persisted.RolledBackAtUtc!.Value);
        Assert.True(persisted.RolledBackAtUtc >= persisted.GrantedAtUtc);
    }

    private static string CreateExpectedGrantFingerprint(
        Guid playerId,
        long expectedRevision,
        string configuredItemId,
        string reason)
    {
        return ComputeSha256Hex(
            $"{{\"command\":\"grant\",\"playerId\":\"{playerId:D}\",\"expectedRevision\":{expectedRevision.ToString(CultureInfo.InvariantCulture)},\"configuredItemId\":\"{configuredItemId}\",\"reason\":\"{reason}\"}}");
    }

    private static string CreateExpectedRollbackFingerprint(
        Guid playerId,
        Guid grantId,
        long expectedRevision,
        string reason)
    {
        return ComputeSha256Hex(
            $"{{\"command\":\"rollback\",\"playerId\":\"{playerId:D}\",\"grantId\":\"{grantId:D}\",\"expectedRevision\":{expectedRevision.ToString(CultureInfo.InvariantCulture)},\"reason\":\"{reason}\"}}");
    }

    private static string ComputeSha256Hex(string canonicalJson)
    {
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson)));
    }

    private void EnsureDisposableDatabaseTargets()
    {
        Assert.Equal(
            DisposableDatabaseMarker,
            RequireEnvironmentVariable(DisposableDatabaseMarkerVariable));
        Assert.Equal(
            ExpectedNovaGameDatabaseName,
            new MySqlConnectionStringBuilder(_novaGameConnectionString).Database);
        Assert.Equal(
            ExpectedSwordMasterDatabaseName,
            new MySqlConnectionStringBuilder(_swordMasterConnectionString).Database);
    }

    private static async Task DrainResponsesAsync(
        IEnumerable<Task<HttpResponseMessage>> requests)
    {
        foreach (var request in requests)
        {
            try
            {
                using var response = await request;
            }
            catch (OperationCanceledException)
            {
            }
            catch (HttpRequestException)
            {
            }
        }
    }

    private static async Task ExecuteNonQueryAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string commandText,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = CreateCommand(connection, transaction, commandText, parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteNonQueryAsync(
        MySqlConnection connection,
        string commandText,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = CreateCommand(connection, transaction: null, commandText, parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> CountAsync(
        MySqlConnection connection,
        string commandText,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = CreateCommand(connection, transaction: null, commandText, parameters);
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<long?> ReadNullableLongAsync(
        MySqlConnection connection,
        string commandText,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = CreateCommand(connection, transaction: null, commandText, parameters);
        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull
            ? null
            : Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static MySqlCommand CreateCommand(
        MySqlConnection connection,
        MySqlTransaction? transaction,
        string commandText,
        params (string Name, object? Value)[] parameters)
    {
        var command = new MySqlCommand(commandText, connection, transaction);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command;
    }

    private static string CollectionPath(Guid playerId)
    {
        return $"/api/swordmaster/operations/player-assets/{playerId:D}/equipment-grants";
    }

    private static string HistoryPath(Guid playerId)
    {
        return $"{CollectionPath(playerId)}?page=1&pageSize=20";
    }

    private static string PlayerEquipmentPath(Guid characterId)
    {
        return $"/api/player-assets/characters/{characterId:D}/equipment/weapon";
    }

    private static Uri CreateApiBaseUri(string apiUrl)
    {
        var normalized = apiUrl.TrimEnd('/') + "/";
        return Uri.TryCreate(normalized, UriKind.Absolute, out var apiBaseUri) &&
            (apiBaseUri.Scheme == Uri.UriSchemeHttp || apiBaseUri.Scheme == Uri.UriSchemeHttps)
            ? apiBaseUri
            : throw new InvalidOperationException(
                "SWORDMASTER_ACCEPTANCE_COMPOSED_API_URL must be an absolute HTTP(S) URL.");
    }

    private static string RequireEnvironmentVariable(string name)
    {
        return Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"{name} is required for composed API acceptance.");
    }

    private static Guid RequireGuidEnvironmentVariable(string name)
    {
        var value = RequireEnvironmentVariable(name);
        return Guid.TryParseExact(value, "D", out var identifier) && identifier != Guid.Empty
            ? identifier
            : throw new InvalidOperationException(
                $"{name} must be a non-empty canonical D-format GUID.");
    }

    private static Guid ParseGuid(string value)
    {
        return Guid.TryParseExact(value, "D", out var identifier) && identifier != Guid.Empty
            ? identifier
            : throw new InvalidOperationException("The disposable database contains an invalid identifier.");
    }

    private static Guid ReadRequiredGuid(MySqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            throw new InvalidOperationException(
                "The disposable database contains a required null identifier.");
        }

        return reader.GetValue(ordinal) switch
        {
            Guid identifier when identifier != Guid.Empty => identifier,
            string value => ParseGuid(value),
            _ => throw new InvalidOperationException(
                "The disposable database contains an invalid identifier representation.")
        };
    }

    private static Guid? GetNullableGuid(MySqlDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal)
            ? null
            : ReadRequiredGuid(reader, ordinal);
    }

    private static DateTime? GetNullableDateTime(MySqlDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);
    }

    private static long? GetNullableLong(MySqlDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
    }

    private static string? GetNullableString(MySqlDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static T Require<T>(T? value)
        where T : class
    {
        Assert.NotNull(value);
        return value;
    }

    private sealed record HistoryResponse(PlayerEquipmentGrantPageDto Page, long Revision);

    private sealed record MutationResponse(
        PlayerEquipmentGrantMutationResultDto Result,
        long Revision);

    private sealed record PlayerState(
        long? Revision,
        long GrantCount,
        long ActiveGrantCount,
        long RolledBackGrantCount,
        long IssuedOwnedItemCount,
        long AssignmentCount,
        long ReceiptCount);

    private sealed record PersistedRolePermission(
        Guid Id,
        Guid RoleId,
        string Name,
        DateTime GrantedAtUtc,
        Guid? GrantedByAdministratorId);

    private sealed record PersistedGrant(
        Guid GrantId,
        Guid PlayerId,
        Guid OwnedItemId,
        string ConfiguredItemId,
        Guid ConfigurationVersionId,
        byte Status,
        Guid GrantedByAdministratorId,
        DateTime GrantedAtUtc,
        string GrantReason,
        Guid GrantIdempotencyKey,
        string GrantRequestFingerprint,
        long GrantResultingRevision,
        string? GrantIpAddress,
        string? GrantUserAgent,
        string? GrantCorrelationId,
        Guid? RolledBackByAdministratorId,
        DateTime? RolledBackAtUtc,
        string? RollbackReason,
        Guid? RollbackIdempotencyKey,
        string? RollbackRequestFingerprint,
        long? RollbackResultingRevision,
        string? RollbackIpAddress,
        string? RollbackUserAgent,
        string? RollbackCorrelationId);
}
