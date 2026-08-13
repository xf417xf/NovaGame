using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NovaCore.BuildingBlocks.Data;
using NovaCore.BuildingBlocks.Infrastructure;
using NovaCore.BuildingBlocks.Modularity;
using NovaCore.BuildingBlocks.Permissions;
using NovaCore.BuildingBlocks.Timing;
using NovaGame.Application.Operations.AdminAccess;
using NovaGame.Domain.Operations.AdminAccess;
using NovaGame.Host.EntityFrameworkCore;
using SwordMaster.Application.Contracts.PlayerAssets;
using SwordMaster.DbMigrator;
using SwordMaster.DbMigrator.PlayerAssets;
using Xunit;

namespace SwordMaster.Integration.Tests.PlayerAssets;

[Collection(RuntimeComposition.SwordMasterRuntimeDatabaseCollection.Name)]
public sealed class PlayerEquipmentGrantPermissionSeedTests
{
    private const string DisposableDatabaseMarkerVariable =
        "SWORDMASTER_TEST_DISPOSABLE_DATABASE_MARKER";
    private const string DisposableDatabaseMarker = "swordmaster-test-disposable-v1";

    [Fact]
    public async Task AdministratorAndPermissionSeedTwiceCreatesOneGrantAndOneAudit()
    {
        RequireDisposableDatabase();
        await using var serviceProvider = CreateServiceProvider();

        await EnsureMigrationsAsync(serviceProvider);
        await RunSeedPipelineAsync(serviceProvider);
        await ResetTargetPermissionAsync(serviceProvider);

        await RunSeedPipelineAsync(serviceProvider);
        await RunSeedPipelineAsync(serviceProvider);

        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NovaGameDbContext>();
        var role = await dbContext.AdminRoles
            .AsNoTracking()
            .SingleAsync(candidate => candidate.NormalizedName == "ADMINISTRATORS");
        Assert.Equal("Administrators", role.Name);
        Assert.True(role.IsBuiltIn);

        var permission = Assert.Single(await dbContext.RolePermissions
            .AsNoTracking()
            .Where(candidate =>
                candidate.RoleId == role.Id &&
                candidate.Name == PlayerEquipmentGrantPermissions.Manage)
            .ToArrayAsync());
        Assert.Equal(role.Id, permission.RoleId);
        Assert.Null(permission.GrantedByAdministratorId);
        Assert.Equal(DateTimeKind.Utc, permission.GrantedAtUtc.Kind);

        var audit = Assert.Single(await dbContext.AdminAccessAuditRecords
            .AsNoTracking()
            .Where(candidate =>
                candidate.Action == AdminAccessAuditAction.PermissionGranted &&
                candidate.RoleId == role.Id &&
                candidate.PermissionName == PlayerEquipmentGrantPermissions.Manage)
            .ToArrayAsync());
        Assert.True(audit.Succeeded);
        Assert.Equal(DateTimeKind.Utc, audit.OccurredAtUtc.Kind);
        Assert.Equal(permission.GrantedAtUtc, audit.OccurredAtUtc);
        Assert.Null(audit.AdministratorId);
        Assert.Null(audit.ActorAdministratorId);
        Assert.Null(audit.SubjectUserName);
        Assert.Null(audit.ErrorCode);
        Assert.Null(audit.IpAddress);
        Assert.Null(audit.UserAgent);
        Assert.Null(audit.CorrelationId);
    }

    [Fact]
    public async Task RunnerRejectsMissingDefinitionAndNonBuiltInAdministratorRole()
    {
        var now = new DateTime(2026, 8, 10, 2, 0, 0, DateTimeKind.Utc);
        var role = new AdminRole(Guid.NewGuid(), "Administrators", now, isBuiltIn: false);
        var auditRepository = new RecordingAuditRepository();

        var missingDefinition = CreateRunner(
            role,
            auditRepository,
            new PermissionDefinitionManager([]),
            now);
        var definitionException = await Assert.ThrowsAsync<InvalidOperationException>(
            () => missingDefinition.SeedAsync());
        Assert.Equal(
            $"Required permission '{PlayerEquipmentGrantPermissions.Manage}' is not defined.",
            definitionException.Message);

        var invalidRole = CreateRunner(
            role,
            auditRepository,
            new PermissionDefinitionManager(
                [new PlayerEquipmentGrantPermissionDefinitionProvider()]),
            now);
        var roleException = await Assert.ThrowsAsync<InvalidOperationException>(
            () => invalidRole.SeedAsync());
        Assert.Equal(
            "The built-in 'Administrators' role is required before seeding SwordMaster permissions.",
            roleException.Message);
        Assert.Empty(auditRepository.Records);
    }

    private static async Task EnsureMigrationsAsync(IServiceProvider serviceProvider)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        await scope.ServiceProvider
            .GetRequiredService<NovaGameDbContext>()
            .Database
            .MigrateAsync();
    }

    private static async Task RunSeedPipelineAsync(IServiceProvider serviceProvider)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NovaGameDbContext>();
        var administratorSeed = scope.ServiceProvider
            .GetServices<IDataSeedContributor>()
            .OfType<AdministratorSeedContributor>()
            .Single();
        var permissionSeed = scope.ServiceProvider
            .GetRequiredService<SwordMasterPlayerEquipmentGrantPermissionSeedRunner>();

        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        await administratorSeed.SeedAsync(new DataSeedContext());
        await dbContext.SaveChangesAsync();
        await permissionSeed.SeedAsync();
        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private static async Task ResetTargetPermissionAsync(IServiceProvider serviceProvider)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NovaGameDbContext>();
        await dbContext.AdminAccessAuditRecords
            .Where(candidate =>
                candidate.Action == AdminAccessAuditAction.PermissionGranted &&
                candidate.PermissionName == PlayerEquipmentGrantPermissions.Manage)
            .ExecuteDeleteAsync();
        await dbContext.RolePermissions
            .Where(candidate => candidate.Name == PlayerEquipmentGrantPermissions.Manage)
            .ExecuteDeleteAsync();
    }

    private static ServiceProvider CreateServiceProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:NovaGame"] = RequireEnvironmentVariable(
                    "SWORDMASTER_TEST_NOVAGAME_MYSQL_CONNECTION_STRING"),
                ["ConnectionStrings:SwordMaster"] = RequireEnvironmentVariable(
                    "SWORDMASTER_TEST_MYSQL_CONNECTION_STRING"),
                ["NovaGame:EntityFrameworkCore:ConnectionStringName"] = "NovaGame",
                ["NovaGame:EntityFrameworkCore:ServerVersion"] = "8.4.0",
                ["SwordMaster:EntityFrameworkCore:ConnectionStringName"] = "SwordMaster",
                ["SwordMaster:EntityFrameworkCore:ServerVersion"] = "8.4.0",
                ["NovaGame:AdminAccess:Seed:UserName"] = "permission-seed-admin",
                ["NovaGame:AdminAccess:Seed:Password"] =
                    "Permission-Seed-Administrator-123!",
                ["NovaGame:AdminAccess:Seed:DisplayName"] =
                    "Permission Seed Administrator"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNovaCoreModules<SwordMasterDbMigratorModule>(
            configuration,
            options => options.AutoDiscoverModules = false);
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = false,
            ValidateScopes = true
        });
    }

    private static SwordMasterPlayerEquipmentGrantPermissionSeedRunner CreateRunner(
        AdminRole role,
        IAdminAccessAuditRepository auditRepository,
        IPermissionDefinitionManager permissionDefinitionManager,
        DateTime now)
    {
        return new SwordMasterPlayerEquipmentGrantPermissionSeedRunner(
            new StubRoleRepository(role),
            auditRepository,
            permissionDefinitionManager,
            new GuidGenerator(),
            new FixedClock(now));
    }

    private static void RequireDisposableDatabase()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable(DisposableDatabaseMarkerVariable),
                DisposableDatabaseMarker,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{DisposableDatabaseMarkerVariable} must mark a disposable database before permission seed test cleanup.");
        }
    }

    private static string RequireEnvironmentVariable(string name)
    {
        return Environment.GetEnvironmentVariable(name) ??
            throw new InvalidOperationException($"{name} is required for permission seed tests.");
    }

    private sealed class StubRoleRepository(AdminRole? role) : IAdminRoleRepository
    {
        public Task<AdminRole?> FindByNormalizedNameAsync(
            string normalizedName,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(role);
        }

        public Task AddAsync(
            AdminRole newRole,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class RecordingAuditRepository : IAdminAccessAuditRepository
    {
        public List<AdminAccessAuditRecord> Records { get; } = [];

        public Task AddAsync(
            AdminAccessAuditRecord auditRecord,
            CancellationToken cancellationToken = default)
        {
            Records.Add(auditRecord);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock(DateTime utcNow) : IClock
    {
        public DateTime Now => utcNow.ToLocalTime();

        public DateTime UtcNow => utcNow;
    }
}
