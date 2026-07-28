using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NovaCore.BuildingBlocks.Data;
using NovaGame.Application.Contracts.Content.GameConfig;
using NovaGame.Domain.Content.GameConfig;
using NovaGame.Domain.Operations.AdminAccess;
using NovaGame.Domain.Shared.Content.GameConfig;
using Xunit;

namespace NovaGame.EntityFrameworkCore.Tests;

[Collection(MySqlDatabaseCollection.Name)]
public sealed class GameConfigApplicationTests
{
    private const string SeedUserName = "admin";
    private const string SeedPassword = "Test-Administrator-Password-123!";
    private const string AuditFailureConstraintName = "ck_game_config_audit_fail_app";
    private const string TransactionConfigurationSet = "app-transaction-rollback";

    private readonly MySqlDatabaseFixture _fixture;

    public GameConfigApplicationTests(MySqlDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DirectApplicationServiceRechecksDatabaseAuthorization()
    {
        await using var factory = CreateFactory();
        var administrator = await SeedAndGetAdministratorAsync(factory);

        var forbidden = await Assert.ThrowsAsync<GameConfigException>(() =>
            ExecuteAsAdministratorAsync(
                factory,
                Guid.NewGuid(),
                "unknown",
                service => service.CreateDraftAsync(
                    CreateDraftRequest(CreateConfigurationSet("authorization")))));
        Assert.Equal(GameConfigFailureKind.Forbidden, forbidden.FailureKind);
        Assert.Equal(GameConfigErrorCodes.Forbidden, forbidden.Code);

        var created = await ExecuteAsAdministratorAsync(
            factory,
            administrator,
            service => service.CreateDraftAsync(
                CreateDraftRequest(CreateConfigurationSet("authorized"))));

        Assert.Equal(administrator.Id, created.CreatedByAdministratorId);
        Assert.Equal(GameConfigDraftState.Editing, created.State);
    }

    [Fact]
    public async Task StaleRevisionMapsToConflictAndUpdateInvalidatesValidation()
    {
        await using var factory = CreateFactory();
        var administrator = await SeedAndGetAdministratorAsync(factory);

        var created = await ExecuteAsAdministratorAsync(
            factory,
            administrator,
            service => service.CreateDraftAsync(
                CreateDraftRequest(CreateConfigurationSet("revision"))));
        var validation = await ExecuteAsAdministratorAsync(
            factory,
            administrator,
            service => service.ValidateDraftAsync(
                created.Id,
                new ValidateGameConfigDraftRequest { ExpectedRevision = created.Revision }));
        Assert.True(validation.IsValid);

        var updated = await ExecuteAsAdministratorAsync(
            factory,
            administrator,
            service => service.UpdateDraftAsync(
                created.Id,
                new UpdateGameConfigDraftRequest
                {
                    ExpectedRevision = created.Revision,
                    Name = created.Name,
                    SchemaVersion = created.SchemaVersion,
                    Content = created.Content
                }));

        Assert.Equal(created.Revision + 1, updated.Revision);
        Assert.Equal(GameConfigDraftState.Editing, updated.State);
        Assert.Null(updated.LatestValidation);

        var conflict = await Assert.ThrowsAsync<GameConfigException>(() =>
            ExecuteAsAdministratorAsync(
                factory,
                administrator,
                service => service.UpdateDraftAsync(
                    created.Id,
                    new UpdateGameConfigDraftRequest
                    {
                        ExpectedRevision = created.Revision,
                        Name = "Stale update",
                        SchemaVersion = created.SchemaVersion,
                        Content = created.Content
                    })));
        Assert.Equal(GameConfigFailureKind.Conflict, conflict.FailureKind);
        Assert.Equal(GameConfigErrorCodes.DraftRevisionConflict, conflict.Code);
    }

    [Fact]
    public async Task PublicationRollsBackWhenTransactionalAuditInsertFails()
    {
        await using var factory = CreateFactory();
        var administrator = await SeedAndGetAdministratorAsync(factory);
        var draft = await ExecuteAsAdministratorAsync(
            factory,
            administrator,
            service => service.CreateDraftAsync(
                CreateDraftRequest(TransactionConfigurationSet)));
        var validation = await ExecuteAsAdministratorAsync(
            factory,
            administrator,
            service => service.ValidateDraftAsync(
                draft.Id,
                new ValidateGameConfigDraftRequest { ExpectedRevision = draft.Revision }));
        Assert.True(validation.IsValid);

        var constraintCreated = false;
        try
        {
            await CreateAuditFailureConstraintAsync();
            constraintCreated = true;
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                ExecuteAsAdministratorAsync(
                    factory,
                    administrator,
                    service => service.PublishDraftAsync(
                    draft.Id,
                    new PublishGameConfigDraftRequest
                    {
                        ExpectedRevision = draft.Revision,
                        ReleaseNotes = "Transaction rollback verification"
                    })));
        }
        finally
        {
            if (constraintCreated)
            {
                await DropAuditFailureConstraintAsync();
            }
        }

        await using var verificationScope = factory.Services.CreateAsyncScope();
        var storedDraft = await verificationScope.ServiceProvider
            .GetRequiredService<IGameConfigDraftRepository>()
            .FindByIdAsync(draft.Id);
        var storedVersion = await verificationScope.ServiceProvider
            .GetRequiredService<IPublishedGameConfigVersionRepository>()
            .FindBySourceRevisionAsync(draft.Id, draft.Revision);

        Assert.NotNull(storedDraft);
        Assert.Null(storedDraft.PublishedVersionId);
        Assert.Null(storedVersion);
    }

    private NovaGameApiFactory CreateFactory()
    {
        return new NovaGameApiFactory(
            _fixture.ConnectionString,
            SeedUserName,
            SeedPassword);
    }

    private async Task<Administrator> SeedAndGetAdministratorAsync(NovaGameApiFactory factory)
    {
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IDataSeedManager>().SeedAsync();
        }

        await using var dbContext = _fixture.CreateDbContext();
        return await dbContext.Administrators.SingleAsync(
            administrator => administrator.NormalizedUserName == SeedUserName.ToUpperInvariant());
    }

    private static Task<T> ExecuteAsAdministratorAsync<T>(
        NovaGameApiFactory factory,
        Administrator administrator,
        Func<IGameConfigAppService, Task<T>> action)
    {
        return ExecuteAsAdministratorAsync(
            factory,
            administrator.Id,
            administrator.UserName,
            action);
    }

    private static async Task<T> ExecuteAsAdministratorAsync<T>(
        NovaGameApiFactory factory,
        Guid administratorId,
        string userName,
        Func<IGameConfigAppService, Task<T>> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        try
        {
            httpContextAccessor.HttpContext = CreateHttpContext(administratorId, userName);
            return await action(scope.ServiceProvider.GetRequiredService<IGameConfigAppService>());
        }
        finally
        {
            httpContextAccessor.HttpContext = null;
        }
    }

    private async Task CreateAuditFailureConstraintAsync()
    {
        await using var dbContext = _fixture.CreateDbContext();
        await dbContext.Database.ExecuteSqlRawAsync($"""
            ALTER TABLE `game_config_audit_records`
            ADD CONSTRAINT `{AuditFailureConstraintName}`
            CHECK (`Action` <> 'Published' OR `ConfigurationSetKey` <> '{TransactionConfigurationSet}')
            """);
    }

    private async Task DropAuditFailureConstraintAsync()
    {
        await using var dbContext = _fixture.CreateDbContext();
        await dbContext.Database.ExecuteSqlRawAsync(
            $"ALTER TABLE `game_config_audit_records` DROP CHECK `{AuditFailureConstraintName}`");
    }

    private static DefaultHttpContext CreateHttpContext(Guid administratorId, string userName)
    {
        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim("sub", administratorId.ToString("D")),
                new Claim("name", userName)
            },
            authenticationType: "GameConfigApplicationTests");
        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity),
            TraceIdentifier = $"game-config-app-{Guid.NewGuid():N}"
        };
    }

    private static CreateGameConfigDraftRequest CreateDraftRequest(string configurationSet)
    {
        return new CreateGameConfigDraftRequest
        {
            ConfigurationSet = configurationSet,
            Name = "Application test configuration",
            SchemaVersion = GameConfigConstants.SupportedSchemaVersion,
            Content = ValidContent
        };
    }

    private static string CreateConfigurationSet(string purpose)
    {
        return $"app-{purpose}-{Guid.NewGuid():N}";
    }

    private const string ValidContent = """
        {
          "collections": {
            "groups": [{ "id": "base" }],
            "records": [
              {
                "id": "entry-a",
                "group": { "$ref": "groups/base" }
              }
            ]
          }
        }
        """;
}
