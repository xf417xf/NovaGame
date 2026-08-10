using System.IdentityModel.Tokens.Jwt;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using NovaCore.Shared.Results;
using NovaCore.Shared.Security;
using NovaGame.Application.Contracts.Content.GameConfig;
using NovaGame.Application.Contracts.Operations.AdminAccess;
using NovaGame.Host.EntityFrameworkCore;
using SwordMaster.Api.PlayerAssets;
using SwordMaster.Application.Contracts.PlayerAssets;
using SwordMaster.Application.PlayerAssets;
using SwordMaster.Domain.PlayerAssets;
using SwordMaster.Host.EntityFrameworkCore;
using SwordMaster.Host.EntityFrameworkCore.PlayerAssets;
using SwordMaster.Integration.Tests.RuntimeComposition;
using Xunit;
using PlayerAssetsContractErrorCodes = SwordMaster.Application.Contracts.PlayerAssets.PlayerAssetsErrorCodes;

namespace SwordMaster.Integration.Tests.PlayerAssets;

[Collection(SwordMasterRuntimeDatabaseCollection.Name)]
public sealed class PlayerAssetsMySqlApiTests
{
    private const string ConfigurationSet = "swordmaster";
    private const string EnvironmentName = "development";
    private const string SchemaVersion = "swordmaster.player-assets.v1";
    private const string PlayerIssuer = "SwordMaster.Runtime.Tests";
    private const string PlayerAudience = "SwordMaster.Player.Runtime.Tests";
    private const string PlayerSigningKey =
        "YWJjZGVmMDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODk";
    private const string DefaultAdministratorUserName = "runtime-admin";
    private const string DefaultAdministratorPassword =
        "Runtime-Administrator-Password-123!";
    private const string DisposableDatabaseMarkerVariable =
        "SWORDMASTER_TEST_DISPOSABLE_DATABASE_MARKER";
    private const string DisposableDatabaseMarker =
        "swordmaster-test-disposable-v1";
    private const string ExpectedNovaGameDatabaseName = "novagame_test";
    private const string ExpectedSwordMasterDatabaseName = "swordmaster_test";

    private static readonly Guid SeedPlayerId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SeedCharacterId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SeedOwnedItemId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");

    private const string VersionOneContent =
        """
        {
          "collections": {
            "equipment-items": [
              {
                "id": "training-sword",
                "slot": "weapon",
                "modifiers": [
                  { "statCode": "attack", "value": 7 },
                  { "statCode": "health", "value": 20 }
                ]
              }
            ]
          }
        }
        """;

    private const string VersionTwoContent =
        """
        {
          "collections": {
            "equipment-items": [
              {
                "id": "training-sword",
                "slot": "weapon",
                "modifiers": [
                  { "statCode": "attack", "value": 11 }
                ]
              }
            ]
          }
        }
        """;

    private readonly string _novaGameConnectionString;
    private readonly string _swordMasterConnectionString;

    public PlayerAssetsMySqlApiTests()
    {
        _novaGameConnectionString = RequireEnvironmentVariable(
            "SWORDMASTER_TEST_NOVAGAME_MYSQL_CONNECTION_STRING");
        _swordMasterConnectionString = RequireEnvironmentVariable(
            "SWORDMASTER_TEST_MYSQL_CONNECTION_STRING");
    }

    [Fact]
    public async Task RealProgramUsesPublishedConfigurationAndPersistsIdempotentEquipmentEffects()
    {
        var transactionBeginGate = new PlayerAssetsTransactionBeginGate();
        var receiptCompletedAtUtc = DateTimeOffset.UtcNow;
        receiptCompletedAtUtc = receiptCompletedAtUtc.AddTicks(
            -(receiptCompletedAtUtc.Ticks % 10));
        await using var factory = new SwordMasterRuntimeApiFactory(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:NovaGame"] = _novaGameConnectionString,
                ["ConnectionStrings:SwordMaster"] = _swordMasterConnectionString
            },
            services =>
            {
                ConfigureGatedTransactionManager(services, transactionBeginGate);
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(
                    new FixedTimeProvider(receiptCompletedAtUtc));
            });
        await ResetStateAsync(factory.Services);

        using var adminClient = CreateHttpsClient(factory);
        var administratorToken = await AuthenticateAdministratorAsync(adminClient);
        var versionOne = await PublishVersionAsync(
            adminClient,
            "mysql-api-version-one",
            VersionOneContent,
            "MySQL API version one");
        var firstActivation = await ActivateVersionAsync(
            adminClient,
            versionOne.Id,
            expectedRevision: 0,
            "Initial MySQL API activation");
        Assert.Equal(1, firstActivation.Revision);
        Assert.Equal(versionOne.Id, firstActivation.ActiveVersionId);

        using var playerClient = CreateHttpsClient(factory);
        using (var anonymousResponse = await playerClient.GetAsync(AttributesPath))
        {
            await AssertUnauthorizedPlayerAsync(anonymousResponse);
        }

        using (var administratorRequest = new HttpRequestMessage(HttpMethod.Get, AttributesPath))
        {
            administratorRequest.Headers.Authorization = Bearer(administratorToken);
            using var administratorResponse = await playerClient.SendAsync(administratorRequest);
            await AssertUnauthorizedPlayerAsync(administratorResponse);
        }

        playerClient.DefaultRequestHeaders.Authorization = Bearer(CreatePlayerToken());
        var equipIdempotencyKey = Guid.NewGuid();
        using var firstEquipRequest = CreateMutationRequest(
            HttpMethod.Put,
            expectedRevision: 1,
            equipIdempotencyKey,
            JsonContent.Create(new EquipEquipmentRequest(SeedOwnedItemId)));
        using var secondEquipRequest = CreateMutationRequest(
            HttpMethod.Put,
            expectedRevision: 1,
            equipIdempotencyKey,
            JsonContent.Create(new EquipEquipmentRequest(SeedOwnedItemId)));
        var firstEquipTask = playerClient.SendAsync(firstEquipRequest);
        var secondEquipTask = playerClient.SendAsync(secondEquipRequest);
        await Task.WhenAll(firstEquipTask, secondEquipTask);
        using (var firstEquipResponse = await firstEquipTask)
        using (var secondEquipResponse = await secondEquipTask)
        {
            await AssertMutationRevisionAsync(firstEquipResponse, 2);
            await AssertMutationRevisionAsync(secondEquipResponse, 2);
        }
        Assert.Equal(2, transactionBeginGate.ArrivalCount);

        await AssertPersistedAssignmentAsync(
            factory.Services,
            expectedRevision: 2,
            expectedConfigurationVersionId: versionOne.Id,
            expectedModifiers: ["attack:7", "health:20"],
            expectedReceiptCount: 1);
        await AssertPersistedReceiptAsync(
            factory.Services,
            equipIdempotencyKey,
            CreateExpectedFingerprint(
                "equip",
                expectedRevision: 1,
                SeedOwnedItemId),
            expectedResultingRevision: 2,
            receiptCompletedAtUtc.UtcDateTime);
        using (var versionOneAttributesResponse = await playerClient.GetAsync(AttributesPath))
        {
            var attributes = await ReadDataAsync<CharacterAttributesDto>(versionOneAttributesResponse);
            AssertStrongEtag(versionOneAttributesResponse, 2);
            AssertEquippedAttributes(
                attributes,
                versionOne.Id,
                attackEquipmentValue: 7,
                healthEquipmentValue: 20);
        }

        var versionTwo = await PublishVersionAsync(
            adminClient,
            "mysql-api-version-two",
            VersionTwoContent,
            "MySQL API version two");
        var secondActivation = await ActivateVersionAsync(
            adminClient,
            versionTwo.Id,
            firstActivation.Revision,
            "Second MySQL API activation");
        Assert.Equal(2, secondActivation.Revision);
        Assert.Equal(versionTwo.Id, secondActivation.ActiveVersionId);
        Assert.Equal(versionOne.Id, secondActivation.PreviousVersionId);

        using (var pinnedAttributesResponse = await playerClient.GetAsync(AttributesPath))
        {
            var attributes = await ReadDataAsync<CharacterAttributesDto>(pinnedAttributesResponse);
            AssertStrongEtag(pinnedAttributesResponse, 2);
            AssertEquippedAttributes(
                attributes,
                versionOne.Id,
                attackEquipmentValue: 7,
                healthEquipmentValue: 20);
        }

        using (var staleRequest = CreateMutationRequest(
                   HttpMethod.Delete,
                   expectedRevision: 1,
                   Guid.NewGuid()))
        using (var staleResponse = await playerClient.SendAsync(staleRequest))
        {
            Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
            Assert.Equal(
                PlayerAssetsContractErrorCodes.ConcurrencyConflict,
                await ReadErrorCodeAsync(staleResponse));
        }

        using (var conflictingReplayRequest = CreateMutationRequest(
                   HttpMethod.Delete,
                   expectedRevision: 2,
                   equipIdempotencyKey))
        using (var conflictingReplayResponse =
               await playerClient.SendAsync(conflictingReplayRequest))
        {
            Assert.Equal(HttpStatusCode.Conflict, conflictingReplayResponse.StatusCode);
            Assert.Equal(
                PlayerAssetsContractErrorCodes.IdempotencyConflict,
                await ReadErrorCodeAsync(conflictingReplayResponse));
        }

        using (var unequipRequest = CreateMutationRequest(
                   HttpMethod.Delete,
                   expectedRevision: 2,
                   Guid.NewGuid()))
        using (var unequipResponse = await playerClient.SendAsync(unequipRequest))
        {
            await AssertMutationRevisionAsync(unequipResponse, 3);
        }

        using (var baseAttributesResponse = await playerClient.GetAsync(AttributesPath))
        {
            var attributes = await ReadDataAsync<CharacterAttributesDto>(baseAttributesResponse);
            AssertStrongEtag(baseAttributesResponse, 3);
            AssertBaseAttributes(attributes);
        }

        using (var reEquipRequest = CreateMutationRequest(
                   HttpMethod.Put,
                   expectedRevision: 3,
                   Guid.NewGuid(),
                   JsonContent.Create(new EquipEquipmentRequest(SeedOwnedItemId))))
        using (var reEquipResponse = await playerClient.SendAsync(reEquipRequest))
        {
            await AssertMutationRevisionAsync(reEquipResponse, 4);
        }

        using (var versionTwoAttributesResponse = await playerClient.GetAsync(AttributesPath))
        {
            var attributes = await ReadDataAsync<CharacterAttributesDto>(versionTwoAttributesResponse);
            AssertStrongEtag(versionTwoAttributesResponse, 4);
            AssertEquippedAttributes(
                attributes,
                versionTwo.Id,
                attackEquipmentValue: 11,
                healthEquipmentValue: 0);
        }

        await AssertPersistedAssignmentAsync(
            factory.Services,
            expectedRevision: 4,
            expectedConfigurationVersionId: versionTwo.Id,
            expectedModifiers: ["attack:11"],
            expectedReceiptCount: 3);
    }

    private static HttpClient CreateHttpsClient(SwordMasterRuntimeApiFactory factory)
    {
        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false
        });
    }

    private static async Task<string> AuthenticateAdministratorAsync(HttpClient client)
    {
        var userName = Environment.GetEnvironmentVariable(
                "NovaGame__AdminAccess__Seed__UserName") ??
            DefaultAdministratorUserName;
        var password = Environment.GetEnvironmentVariable(
                "NovaGame__AdminAccess__Seed__Password") ??
            DefaultAdministratorPassword;
        using var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest
            {
                UserName = userName,
                Password = password
            });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var accessToken = (await ReadDataAsync<AccessTokenResponse>(response)).AccessToken;
        client.DefaultRequestHeaders.Authorization = Bearer(accessToken);
        return accessToken;
    }

    private static async Task<GameConfigVersionDto> PublishVersionAsync(
        HttpClient client,
        string name,
        string content,
        string releaseNotes)
    {
        using var createResponse = await client.PostAsJsonAsync(
            "/api/game-config/drafts",
            new CreateGameConfigDraftRequest
            {
                ConfigurationSet = ConfigurationSet,
                Name = name,
                SchemaVersion = SchemaVersion,
                Content = content
            });
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var draft = await ReadDataAsync<GameConfigDraftDto>(createResponse);

        using var validationResponse = await client.PostAsJsonAsync(
            $"/api/game-config/drafts/{draft.Id:D}/validate",
            new ValidateGameConfigDraftRequest { ExpectedRevision = draft.Revision });
        Assert.Equal(HttpStatusCode.OK, validationResponse.StatusCode);
        var validation = await ReadDataAsync<GameConfigValidationResultDto>(validationResponse);
        Assert.True(validation.IsValid);
        Assert.Empty(validation.Issues);

        using var publishResponse = await client.PostAsJsonAsync(
            $"/api/game-config/drafts/{draft.Id:D}/publish",
            new PublishGameConfigDraftRequest
            {
                ExpectedRevision = draft.Revision,
                ReleaseNotes = releaseNotes
            });
        Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);
        return await ReadDataAsync<GameConfigVersionDto>(publishResponse);
    }

    private static async Task<GameConfigEnvironmentDto> ActivateVersionAsync(
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

    private static async Task ResetStateAsync(IServiceProvider services)
    {
        RequireDisposableDatabaseTargets(services);

        await using (var scope = services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<NovaGameDbContext>();
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

        await using (var scope = services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<SwordMasterDbContext>();
            var playerId = PlayerId.From(SeedPlayerId);
            var existingProfile = await dbContext.PlayerAssetsProfiles
                .Include(profile => profile.Character)
                    .ThenInclude(character => character.BaseAttributes)
                .Include(profile => profile.OwnedItems)
                .Include(profile => profile.EquipmentAssignments)
                .AsSplitQuery()
                .SingleOrDefaultAsync(profile => profile.Id == SeedPlayerId);
            var receipts = await dbContext.PlayerAssetCommandReceipts
                .Where(receipt => receipt.PlayerId == playerId)
                .ToListAsync();
            dbContext.PlayerAssetCommandReceipts.RemoveRange(receipts);
            if (existingProfile is not null)
            {
                dbContext.PlayerAssetsProfiles.Remove(existingProfile);
            }

            await dbContext.SaveChangesAsync();
            dbContext.ChangeTracker.Clear();
            await dbContext.PlayerAssetsProfiles.AddAsync(new PlayerAssetsProfile(
                playerId,
                new PlayerCharacter(
                    CharacterId.From(SeedCharacterId),
                    [
                        AttributeValue.Create("attack", 10),
                        AttributeValue.Create("health", 100)
                    ]),
                [
                    new OwnedEquipmentItem(
                        OwnedItemId.From(SeedOwnedItemId),
                        "training-sword")
                ]));
            await dbContext.SaveChangesAsync();
        }
    }

    private static void RequireDisposableDatabaseTargets(IServiceProvider services)
    {
        var marker = Environment.GetEnvironmentVariable(
            DisposableDatabaseMarkerVariable);
        if (!string.Equals(
                marker,
                DisposableDatabaseMarker,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{DisposableDatabaseMarkerVariable} must explicitly mark the disposable SwordMaster database scope before test data can be deleted.");
        }

        using var scope = services.CreateScope();
        var novaGameDatabaseName = scope.ServiceProvider
            .GetRequiredService<NovaGameDbContext>()
            .Database.GetDbConnection().Database;
        var swordMasterDatabaseName = scope.ServiceProvider
            .GetRequiredService<SwordMasterDbContext>()
            .Database.GetDbConnection().Database;
        if (!string.Equals(
                novaGameDatabaseName,
                ExpectedNovaGameDatabaseName,
                StringComparison.Ordinal) ||
            !string.Equals(
                swordMasterDatabaseName,
                ExpectedSwordMasterDatabaseName,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"MySQL API test cleanup requires exact disposable databases '{ExpectedNovaGameDatabaseName}' and '{ExpectedSwordMasterDatabaseName}', but resolved '{novaGameDatabaseName}' and '{swordMasterDatabaseName}'.");
        }
    }

    private static void ConfigureGatedTransactionManager(
        IServiceCollection services,
        PlayerAssetsTransactionBeginGate gate)
    {
        services.RemoveAll<IPlayerAssetsTransactionManager>();
        services.RemoveAll<PlayerAssetsTransactionManager>();
        services.AddScoped<PlayerAssetsTransactionManager>();
        services.AddScoped<IPlayerAssetsTransactionManager>(serviceProvider =>
            new GatedPlayerAssetsTransactionManager(
                serviceProvider.GetRequiredService<PlayerAssetsTransactionManager>(),
                gate));
    }

    private static async Task AssertPersistedAssignmentAsync(
        IServiceProvider services,
        long expectedRevision,
        Guid expectedConfigurationVersionId,
        IReadOnlyList<string> expectedModifiers,
        int expectedReceiptCount)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SwordMasterDbContext>();
        var playerId = PlayerId.From(SeedPlayerId);
        var repository = new PlayerAssetsProfileRepository(dbContext);
        var profile = Require(await repository.FindByPlayerIdAsync(playerId));
        var assignment = Assert.Single(profile.EquipmentAssignments);

        Assert.Equal(expectedRevision, profile.Revision);
        Assert.Equal(expectedConfigurationVersionId, assignment.ConfigurationVersionId);
        Assert.Equal(SeedOwnedItemId, assignment.OwnedItemId.Value);
        Assert.Equal(
            expectedModifiers,
            assignment.Modifiers
                .OrderBy(modifier => modifier.Code.Value, StringComparer.Ordinal)
                .Select(modifier =>
                    $"{modifier.Code.Value}:{modifier.Value.Value}")
                .ToArray());
        Assert.Equal(
            expectedReceiptCount,
            await dbContext.PlayerAssetCommandReceipts.CountAsync(
                receipt => receipt.PlayerId == playerId));
    }

    private static async Task AssertPersistedReceiptAsync(
        IServiceProvider services,
        Guid expectedIdempotencyKey,
        string expectedRequestFingerprint,
        long expectedResultingRevision,
        DateTime expectedCompletedAtUtc)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SwordMasterDbContext>();
        var receipt = await dbContext.PlayerAssetCommandReceipts
            .AsNoTracking()
            .SingleAsync(candidate =>
                candidate.PlayerId == PlayerId.From(SeedPlayerId) &&
                candidate.IdempotencyKey == expectedIdempotencyKey);

        Assert.Equal(SeedPlayerId, receipt.PlayerId.Value);
        Assert.Equal(expectedIdempotencyKey, receipt.IdempotencyKey);
        Assert.Equal(expectedRequestFingerprint, receipt.RequestFingerprint);
        Assert.Equal(expectedResultingRevision, receipt.ResultingRevision);
        Assert.Equal(DateTimeKind.Utc, receipt.CompletedAtUtc.Kind);
        Assert.Equal(expectedCompletedAtUtc, receipt.CompletedAtUtc);
    }

    private static string CreateExpectedFingerprint(
        string command,
        long expectedRevision,
        Guid? ownedItemId = null)
    {
        var canonical = string.Join(
            '|',
            command,
            SeedCharacterId.ToString("D"),
            "weapon",
            expectedRevision.ToString(CultureInfo.InvariantCulture),
            ownedItemId?.ToString("D") ?? string.Empty);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static HttpRequestMessage CreateMutationRequest(
        HttpMethod method,
        long expectedRevision,
        Guid idempotencyKey,
        HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, EquipmentPath)
        {
            Content = content
        };
        request.Headers.TryAddWithoutValidation(
            PlayerAssetsController.IfMatchHeaderName,
            $"\"{expectedRevision}\"");
        request.Headers.TryAddWithoutValidation(
            PlayerAssetsController.IdempotencyKeyHeaderName,
            idempotencyKey.ToString("D"));
        return request;
    }

    private static async Task AssertUnauthorizedPlayerAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(
            PlayerAssetsContractErrorCodes.UnauthorizedPlayer,
            await ReadErrorCodeAsync(response));
        Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
    }

    private static async Task AssertMutationRevisionAsync(
        HttpResponseMessage response,
        long expectedRevision)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await ReadDataAsync<PlayerAssetsMutationResultDto>(response);
        Assert.Equal(expectedRevision, result.Revision);
        AssertStrongEtag(response, expectedRevision);
    }

    private static void AssertEquippedAttributes(
        CharacterAttributesDto attributes,
        Guid configurationVersionId,
        int attackEquipmentValue,
        int healthEquipmentValue)
    {
        Assert.Equal(SeedCharacterId, attributes.CharacterId);
        var attack = Assert.Single(
            attributes.Attributes,
            attribute => attribute.StatCode == "attack");
        Assert.Equal(10, attack.BaseValue);
        Assert.Equal(attackEquipmentValue, attack.EquipmentValue);
        Assert.Equal(10 + attackEquipmentValue, attack.EffectiveValue);
        var health = Assert.Single(
            attributes.Attributes,
            attribute => attribute.StatCode == "health");
        Assert.Equal(100, health.BaseValue);
        Assert.Equal(healthEquipmentValue, health.EquipmentValue);
        Assert.Equal(100 + healthEquipmentValue, health.EffectiveValue);
        var assignment = Assert.Single(attributes.EquipmentAssignments);
        Assert.Equal("weapon", assignment.Slot);
        Assert.Equal(SeedOwnedItemId, assignment.OwnedItemId);
        Assert.Equal("training-sword", assignment.ConfiguredItemId);
        Assert.Equal(configurationVersionId, assignment.ConfigurationVersionId);
    }

    private static void AssertBaseAttributes(CharacterAttributesDto attributes)
    {
        Assert.Equal(SeedCharacterId, attributes.CharacterId);
        Assert.Empty(attributes.EquipmentAssignments);
        Assert.Equal(
            [
                new CharacterAttributeDto("attack", 10, 0, 10),
                new CharacterAttributeDto("health", 100, 0, 100)
            ],
            attributes.Attributes);
    }

    private static void AssertStrongEtag(HttpResponseMessage response, long revision)
    {
        Assert.NotNull(response.Headers.ETag);
        Assert.False(response.Headers.ETag.IsWeak);
        Assert.Equal($"\"{revision}\"", response.Headers.ETag.Tag);
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
                $"{name} is required for SwordMaster MySQL API integration tests.");
    }

    private static T Require<T>(T? value)
        where T : class
    {
        Assert.NotNull(value);
        return value;
    }

    private static string EquipmentPath =>
        $"/api/player-assets/characters/{SeedCharacterId:D}/equipment/weapon";

    private static string AttributesPath =>
        $"/api/player-assets/characters/{SeedCharacterId:D}/attributes";

    private sealed class GatedPlayerAssetsTransactionManager :
        IPlayerAssetsTransactionManager
    {
        private readonly PlayerAssetsTransactionManager _inner;
        private readonly PlayerAssetsTransactionBeginGate _gate;

        public GatedPlayerAssetsTransactionManager(
            PlayerAssetsTransactionManager inner,
            PlayerAssetsTransactionBeginGate gate)
        {
            _inner = inner;
            _gate = gate;
        }

        public async Task<IPlayerAssetsTransactionScope> BeginAsync(
            CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken);
            return await _inner.BeginAsync(cancellationToken);
        }
    }

    private sealed class PlayerAssetsTransactionBeginGate
    {
        private static readonly TimeSpan ArrivalTimeout = TimeSpan.FromSeconds(10);

        private readonly TaskCompletionSource<bool> _bothRequestsArrived =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivalCount;

        public int ArrivalCount => Volatile.Read(ref _arrivalCount);

        public async Task WaitAsync(CancellationToken cancellationToken)
        {
            var arrival = Interlocked.Increment(ref _arrivalCount);
            if (arrival > 2)
            {
                return;
            }

            if (arrival == 2)
            {
                _bothRequestsArrived.TrySetResult(true);
            }

            try
            {
                await _bothRequestsArrived.Task.WaitAsync(
                    ArrivalTimeout,
                    cancellationToken);
            }
            catch (TimeoutException)
            {
                var exception = new TimeoutException(
                    "Both concurrent equipment requests did not reach the transaction boundary before the test timeout.");
                _bothRequestsArrived.TrySetException(exception);
                throw exception;
            }
            catch (OperationCanceledException)
            {
                _bothRequestsArrived.TrySetCanceled(cancellationToken);
                throw;
            }
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
