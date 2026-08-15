using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MySqlConnector;
using NovaCore.BuildingBlocks.Data;
using NovaCore.BuildingBlocks.Modularity;
using NovaCore.BuildingBlocks.Permissions;
using NovaCore.BuildingBlocks.Timing;
using NovaGame.DbMigrator;
using NovaGame.Domain.Operations.AdminAccess;
using NovaGame.Domain.Shared.Content.GameConfig;
using NovaGame.Domain.Shared.Operations.AdminAccess;
using NovaGame.Host.EntityFrameworkCore;
using Xunit;

namespace NovaGame.EntityFrameworkCore.Tests;

[Collection(MySqlDatabaseCollection.Name)]
public sealed class AdminAccessManagementSeedTests
{
    private const string SeedUserName = "seed-admin";
    private const string SeedPassword = "Test-Administrator-Password-123!";
    private static readonly DateTime SeedTimestamp = new(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);

    private static readonly string[] ExpectedAdminAccessPermissionNames =
    new[]
    {
        AdminAccessPermissions.Access,
        AdminAccessPermissions.AdministratorsManage,
        AdminAccessPermissions.AdministratorsRead,
        AdminAccessPermissions.AdministratorsResetPassword,
        AdminAccessPermissions.AuditLogsRead,
        AdminAccessPermissions.RolesManage,
        AdminAccessPermissions.RolesRead,
        AdminAccessPermissions.SessionsRead,
        AdminAccessPermissions.SessionsRevoke
    }
        .Order(StringComparer.Ordinal)
        .ToArray();

    private readonly MySqlDatabaseFixture _fixture;

    public AdminAccessManagementSeedTests(MySqlDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DbMigratorSynchronizesTheEntireRuntimeCatalogAndSecondRunIsAnExactNoOp()
    {
        await ResetAdminAccessAsync();

        try
        {
            await using var provider = CreateDbMigratorProvider();
            var registeredPermissionNames = GetRegisteredPermissionNames(provider);

            Assert.Equal(
                ExpectedAdminAccessPermissionNames,
                registeredPermissionNames
                    .Where(name => name.StartsWith(AdminAccessPermissions.GroupName + ".", StringComparison.Ordinal))
                    .ToArray());
            Assert.Contains(GameConfigPermissions.Access, registeredPermissionNames);
            Assert.Contains(GameConfigPermissions.ManageDrafts, registeredPermissionNames);
            Assert.Contains(GameConfigPermissions.Publish, registeredPermissionNames);
            Assert.Contains(GameConfigPermissions.Activate, registeredPermissionNames);

            await SeedAsync(provider);
            var first = await ReadSeedStateAsync();
            var role = Assert.Single(first.Roles);

            Assert.Equal(AdminAccessManagementPolicy.BuiltInRoleName, role.Name);
            Assert.Equal(AdminAccessManagementPolicy.BuiltInRoleNormalizedName, role.NormalizedName);
            Assert.True(role.IsBuiltIn);
            Assert.True(role.IsActive);
            Assert.Equal(1L, role.Revision);
            Assert.Single(first.Administrators);
            Assert.Single(first.Memberships);
            Assert.Equal(role.Id, first.Memberships[0].RoleId);
            Assert.Equal(registeredPermissionNames, first.Grants.Select(grant => grant.Name).ToArray());

            var grantAudits = first.Audits
                .Where(audit => audit.Action == AdminAccessAuditAction.PermissionGranted)
                .OrderBy(audit => audit.PermissionName, StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(registeredPermissionNames, grantAudits.Select(audit => audit.PermissionName!).ToArray());
            Assert.All(grantAudits, audit =>
            {
                Assert.True(audit.Succeeded);
                Assert.Equal(role.Id, audit.RoleId);
                Assert.Equal(role.Revision, audit.TargetRevision);
            });
            Assert.Single(first.Audits, audit => audit.Action == AdminAccessAuditAction.RoleCreated);
            Assert.Single(first.Audits, audit => audit.Action == AdminAccessAuditAction.AdministratorCreated);
            Assert.Single(first.Audits, audit => audit.Action == AdminAccessAuditAction.RoleAssigned);

            await SeedAsync(provider);
            var second = await ReadSeedStateAsync();

            AssertSeedStatesEqual(first, second);
        }
        finally
        {
            await ResetAdminAccessAsync();
        }
    }

    [Fact]
    public async Task DbMigratorAddsAndRemovesStoredGrantsInOneRevisionAndAuditsEachDelta()
    {
        await ResetAdminAccessAsync();

        try
        {
            var originalCatalog = CreatePermissionDefinitionManager(
                ("Zeta.Runtime", "Runtime.Zeta"),
                ("Alpha.Runtime", "Runtime.Alpha"),
                ("Zeta.Runtime", "Runtime.Beta"));
            var changedCatalog = CreatePermissionDefinitionManager(
                ("Omega.Runtime", "Runtime.Omega"),
                ("Alpha.Runtime", "Runtime.Alpha"),
                ("Zeta.Runtime", "Runtime.Zeta"));

            await using (var originalProvider = CreateDbMigratorProvider(originalCatalog))
            {
                await SeedAsync(originalProvider);
            }

            var before = await ReadSeedStateAsync();
            var beforeRole = Assert.Single(before.Roles);
            var retainedGrant = Assert.Single(before.Grants, grant => grant.Name == "Runtime.Alpha");
            var removedGrant = Assert.Single(before.Grants, grant => grant.Name == "Runtime.Beta");

            await using (var changedProvider = CreateDbMigratorProvider(changedCatalog))
            {
                await SeedAsync(changedProvider);
            }

            var after = await ReadSeedStateAsync();
            var afterRole = Assert.Single(after.Roles);
            var retainedAfterGrant = Assert.Single(after.Grants, grant => grant.Name == retainedGrant.Name);

            Assert.Equal(beforeRole.Id, afterRole.Id);
            Assert.Equal(beforeRole.Revision + 1, afterRole.Revision);
            Assert.Equal(
                new[] { "Runtime.Alpha", "Runtime.Omega", "Runtime.Zeta" },
                after.Grants.Select(grant => grant.Name).ToArray());
            Assert.Equal(retainedGrant.Id, retainedAfterGrant.Id);
            Assert.Equal(retainedGrant.GrantedAtUtc, retainedAfterGrant.GrantedAtUtc);
            Assert.DoesNotContain(after.Grants, grant => grant.Id == removedGrant.Id);

            var beforeAuditIds = before.Audits.Select(audit => audit.Id).ToHashSet();
            var deltaAudits = after.Audits
                .Where(audit => !beforeAuditIds.Contains(audit.Id))
                .OrderBy(audit => audit.Action)
                .ThenBy(audit => audit.PermissionName, StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(2, deltaAudits.Length);
            Assert.Contains(
                deltaAudits,
                audit => audit.Action == AdminAccessAuditAction.PermissionGranted &&
                    audit.PermissionName == "Runtime.Omega");
            Assert.Contains(
                deltaAudits,
                audit => audit.Action == AdminAccessAuditAction.PermissionRevoked &&
                    audit.PermissionName == "Runtime.Beta");
            Assert.All(deltaAudits, audit =>
            {
                Assert.True(audit.Succeeded);
                Assert.Equal(afterRole.Id, audit.RoleId);
                Assert.Equal(afterRole.Revision, audit.TargetRevision);
            });
        }
        finally
        {
            await ResetAdminAccessAsync();
        }
    }

    [Fact]
    public async Task DbMigratorFailsClosedWithoutWritingWhenExistingAdministratorsHaveNoBuiltInRole()
    {
        await ResetAdminAccessAsync();

        try
        {
            await AddExistingAdministratorAsync();
            var before = await ReadSeedStateAsync();
            await using var provider = CreateDbMigratorProvider(CreateValidTestCatalog());

            await AssertInvariantViolationAsync(() => SeedAsync(provider));

            var after = await ReadSeedStateAsync();
            AssertSeedStatesEqual(before, after);
            Assert.Single(after.Administrators);
            Assert.Empty(after.Roles);
            Assert.Empty(after.Grants);
            Assert.Empty(after.Audits);
        }
        finally
        {
            await ResetAdminAccessAsync();
        }
    }

    [Fact]
    public async Task DbMigratorFailsClosedWithoutWritingWhenTheStoredBuiltInRoleIsMalformed()
    {
        await ResetAdminAccessAsync();

        try
        {
            await AddExistingAdministratorAsync();
            await AddMalformedBuiltInRoleAsync();
            var before = await ReadSeedStateAsync();
            await using var provider = CreateDbMigratorProvider(CreateValidTestCatalog());

            await AssertInvariantViolationAsync(() => SeedAsync(provider));

            var after = await ReadSeedStateAsync();
            AssertSeedStatesEqual(before, after);
            var malformedRole = Assert.Single(after.Roles);
            Assert.True(malformedRole.IsBuiltIn);
            Assert.Equal(Guid.Empty, malformedRole.Id);
            Assert.Empty(after.Grants);
            Assert.Empty(after.Audits);
        }
        finally
        {
            await ResetAdminAccessAsync();
        }
    }

    [Fact]
    public async Task DbMigratorRejectsAMalformedRuntimeCatalogWithoutChangingStoredSeedState()
    {
        await ResetAdminAccessAsync();

        try
        {
            var validCatalog = CreateValidTestCatalog();
            await using (var bootstrapProvider = CreateDbMigratorProvider(validCatalog))
            {
                await SeedAsync(bootstrapProvider);
            }

            var before = await ReadSeedStateAsync();
            var malformedCatalog = CreatePermissionDefinitionManager(
                ("Alpha.Runtime", "Runtime.Duplicate"),
                ("Beta.Runtime", "Runtime.Duplicate"));
            await using var malformedProvider = CreateDbMigratorProvider(malformedCatalog);

            await AssertInvariantViolationAsync(() => SeedAsync(malformedProvider));

            var after = await ReadSeedStateAsync();
            AssertSeedStatesEqual(before, after);
        }
        finally
        {
            await ResetAdminAccessAsync();
        }
    }

    [Fact]
    public async Task ConcurrentDbMigratorSeedWritersMakeTheLockWaitLoserRollBackWithoutPartialRows()
    {
        await ResetAdminAccessAsync();

        var race = new SeedWriterRace();
        Task<SeedRunOutcome>? firstRun = null;

        try
        {
            var catalog = CreateValidTestCatalog();
            await using var firstProvider = CreateDbMigratorProvider(
                catalog,
                firstWriterRace: race);
            await using var secondProvider = CreateDbMigratorProvider(
                catalog,
                configureLockWaitTimeout: true);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            firstRun = Task.Run(() => RunSeedAsync(firstProvider, cancellation.Token));
            await race.FirstWriterFlushed.Task.WaitAsync(cancellation.Token);

            var secondOutcome = await Task.Run(() => RunSeedAsync(secondProvider, cancellation.Token));
            Assert.False(secondOutcome.Succeeded);
            Assert.NotNull(secondOutcome.Exception);
            Assert.True(ContainsMySqlLockWaitTimeout(secondOutcome.Exception));

            var whileFirstWriterIsHeld = await ReadSeedStateAsync();
            Assert.Empty(whileFirstWriterIsHeld.Administrators);
            Assert.Empty(whileFirstWriterIsHeld.Roles);
            Assert.Empty(whileFirstWriterIsHeld.Memberships);
            Assert.Empty(whileFirstWriterIsHeld.Grants);
            Assert.Empty(whileFirstWriterIsHeld.Audits);

            race.ReleaseFirstWriter.TrySetResult(true);
            var firstOutcome = await firstRun;
            Assert.True(firstOutcome.Succeeded);

            var state = await ReadSeedStateAsync();
            var role = Assert.Single(state.Roles);
            Assert.True(role.IsBuiltIn);
            Assert.Equal(GetRegisteredPermissionNames(catalog), state.Grants.Select(grant => grant.Name).ToArray());
            Assert.Single(state.Administrators);
            Assert.Single(state.Memberships);
            Assert.Equal(role.Id, state.Memberships[0].RoleId);
            Assert.Equal(
                state.Grants.Count,
                state.Audits.Count(audit => audit.Action == AdminAccessAuditAction.PermissionGranted));
            Assert.Single(state.Audits, audit => audit.Action == AdminAccessAuditAction.RoleCreated);
            Assert.Single(state.Audits, audit => audit.Action == AdminAccessAuditAction.AdministratorCreated);
            Assert.Single(state.Audits, audit => audit.Action == AdminAccessAuditAction.RoleAssigned);
            Assert.All(
                state.Audits.Where(audit => audit.Action == AdminAccessAuditAction.PermissionGranted),
                audit => Assert.Equal(role.Revision, audit.TargetRevision));
        }
        finally
        {
            race.ReleaseFirstWriter.TrySetResult(true);
            if (firstRun is not null)
            {
                await firstRun;
            }

            await ResetAdminAccessAsync();
        }
    }

    private ServiceProvider CreateDbMigratorProvider(
        IPermissionDefinitionManager? permissionDefinitionManager = null,
        SeedWriterRace? firstWriterRace = null,
        bool configureLockWaitTimeout = false)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _fixture.ConnectionString,
                ["NovaGame:EntityFrameworkCore:ConnectionStringName"] = "Default",
                ["NovaGame:EntityFrameworkCore:ServerVersion"] = "8.4.0",
                ["NovaGame:AdminAccess:Seed:UserName"] = SeedUserName,
                ["NovaGame:AdminAccess:Seed:Password"] = SeedPassword,
                ["NovaGame:AdminAccess:Seed:DisplayName"] = "Seed Test Administrator"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNovaCoreModules<NovaGameDbMigratorModule>(configuration);
        services.RemoveAll<IClock>();
        services.AddSingleton<IClock>(new FixedClock(SeedTimestamp));

        if (permissionDefinitionManager is not null)
        {
            services.RemoveAll<IPermissionDefinitionManager>();
            services.AddSingleton(permissionDefinitionManager);
        }

        if (configureLockWaitTimeout)
        {
            services.AddScoped<IDataSeedContributor, LockWaitTimeoutSeedContributor>();
        }

        if (firstWriterRace is not null)
        {
            services.AddScoped<IDataSeedContributor>(serviceProvider =>
                new BlockingFirstWriterSeedContributor(
                    serviceProvider.GetRequiredService<NovaGameDbContext>(),
                    firstWriterRace));
        }

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true
        });
    }

    private static async Task SeedAsync(
        ServiceProvider provider,
        CancellationToken cancellationToken = default)
    {
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider
            .GetRequiredService<IDataSeedManager>()
            .SeedAsync(cancellationToken: cancellationToken);
    }

    private static async Task<SeedRunOutcome> RunSeedAsync(
        ServiceProvider provider,
        CancellationToken cancellationToken)
    {
        try
        {
            await SeedAsync(provider, cancellationToken);
            return new SeedRunOutcome(true, null);
        }
        catch (Exception exception)
        {
            return new SeedRunOutcome(false, exception);
        }
    }

    private async Task AddExistingAdministratorAsync()
    {
        await using var dbContext = _fixture.CreateDbContext();
        dbContext.Administrators.Add(new Administrator(
            Guid.NewGuid(),
            AdministratorUserName.Create("existing-administrator"),
            "Existing administrator",
            "existing-password-hash",
            SeedTimestamp));
        await dbContext.SaveChangesAsync();
    }

    private async Task AddMalformedBuiltInRoleAsync()
    {
        await using var dbContext = _fixture.CreateDbContext();
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO `admin_roles` (
                `Id`, `Name`, `NormalizedName`, `IsBuiltIn`, `CreatedAtUtc`, `IsActive`, `Revision`)
            VALUES (
                {Guid.Empty.ToString("D")}, {AdminAccessManagementPolicy.BuiltInRoleName},
                {AdminAccessManagementPolicy.BuiltInRoleNormalizedName}, {true}, {SeedTimestamp}, {true}, {1L})
            """);
    }

    private async Task<SeedState> ReadSeedStateAsync()
    {
        await using var dbContext = _fixture.CreateDbContext();
        var administrators = await dbContext.Administrators
            .AsNoTracking()
            .OrderBy(administrator => administrator.Id)
            .ToArrayAsync();
        var roles = await dbContext.AdminRoles
            .AsNoTracking()
            .OrderBy(role => role.Id)
            .ToArrayAsync();
        var memberships = await dbContext.AdministratorRoles
            .AsNoTracking()
            .OrderBy(membership => membership.Id)
            .ToArrayAsync();
        var grants = await dbContext.RolePermissions
            .AsNoTracking()
            .OrderBy(permission => permission.Name)
            .ThenBy(permission => permission.Id)
            .ToArrayAsync();
        var audits = await dbContext.AdminAccessAuditRecords
            .AsNoTracking()
            .OrderBy(audit => audit.OccurredAtUtc)
            .ThenBy(audit => audit.Id)
            .ToArrayAsync();

        return new SeedState(
            administrators.Select(administrator => new AdministratorState(
                administrator.Id,
                administrator.UserName,
                administrator.NormalizedUserName,
                administrator.DisplayName,
                administrator.PasswordHash,
                administrator.IsActive,
                administrator.CreatedAtUtc,
                administrator.UpdatedAtUtc,
                administrator.Revision)).ToArray(),
            roles.Select(role => new RoleState(
                role.Id,
                role.Name,
                role.NormalizedName,
                role.IsBuiltIn,
                role.IsActive,
                role.CreatedAtUtc,
                role.UpdatedAtUtc,
                role.Revision)).ToArray(),
            memberships.Select(membership => new MembershipState(
                membership.Id,
                membership.AdministratorId,
                membership.RoleId,
                membership.AssignedAtUtc,
                membership.AssignedByAdministratorId)).ToArray(),
            grants.Select(grant => new GrantState(
                grant.Id,
                grant.RoleId,
                grant.Name,
                grant.GrantedAtUtc,
                grant.GrantedByAdministratorId)).ToArray(),
            audits.Select(audit => new AuditState(
                audit.Id,
                audit.Action,
                audit.OccurredAtUtc,
                audit.Succeeded,
                audit.AdministratorId,
                audit.ActorAdministratorId,
                audit.RoleId,
                audit.PermissionName,
                audit.TargetRevision)).ToArray());
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

    private static async Task AssertInvariantViolationAsync(Func<Task> operation)
    {
        var exception = await Assert.ThrowsAsync<AdminAccessManagementDomainException>(operation);
        Assert.Equal(AdminAccessManagementErrorCodes.InvariantViolation, exception.Code);
    }

    private static void AssertSeedStatesEqual(SeedState expected, SeedState actual)
    {
        Assert.Equal(expected.Administrators, actual.Administrators);
        Assert.Equal(expected.Roles, actual.Roles);
        Assert.Equal(expected.Memberships, actual.Memberships);
        Assert.Equal(expected.Grants, actual.Grants);
        Assert.Equal(expected.Audits, actual.Audits);
    }

    private static bool ContainsMySqlLockWaitTimeout(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is MySqlException { Number: 1205 })
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<string> GetRegisteredPermissionNames(IServiceProvider provider)
    {
        return GetRegisteredPermissionNames(
            provider.GetRequiredService<IPermissionDefinitionManager>());
    }

    private static IReadOnlyList<string> GetRegisteredPermissionNames(
        IPermissionDefinitionManager permissionDefinitionManager)
    {
        return permissionDefinitionManager.Groups
            .OrderBy(group => group.Name, StringComparer.Ordinal)
            .SelectMany(group => group.Permissions
                .OrderBy(permission => permission.Name, StringComparer.Ordinal)
                .Select(permission => permission.Name))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static TestPermissionDefinitionManager CreateValidTestCatalog()
    {
        return CreatePermissionDefinitionManager(
            ("Zeta.Runtime", "Runtime.Zeta"),
            ("Alpha.Runtime", "Runtime.Alpha"),
            ("Zeta.Runtime", "Runtime.Beta"));
    }

    private static TestPermissionDefinitionManager CreatePermissionDefinitionManager(
        params (string GroupName, string PermissionName)[] definitions)
    {
        var groups = new List<PermissionGroupDefinition>();
        foreach (var group in definitions.GroupBy(definition => definition.GroupName, StringComparer.Ordinal))
        {
            var definition = new PermissionGroupDefinition(group.Key, group.Key);
            foreach (var permission in group)
            {
                definition.AddPermission(permission.PermissionName, permission.PermissionName);
            }

            groups.Add(definition);
        }

        return new TestPermissionDefinitionManager(groups);
    }

    private sealed class FixedClock(DateTime utcNow) : IClock
    {
        public DateTime Now => utcNow.ToLocalTime();

        public DateTime UtcNow => utcNow;
    }

    private sealed class LockWaitTimeoutSeedContributor(NovaGameDbContext dbContext) : IDataSeedContributor
    {
        public int Order => -100;

        public Task SeedAsync(DataSeedContext context, CancellationToken cancellationToken = default)
        {
            return dbContext.Database.ExecuteSqlRawAsync(
                "SET SESSION innodb_lock_wait_timeout = 1",
                cancellationToken);
        }
    }

    private sealed class BlockingFirstWriterSeedContributor(
        NovaGameDbContext dbContext,
        SeedWriterRace race) : IDataSeedContributor
    {
        public int Order => 101;

        public async Task SeedAsync(DataSeedContext context, CancellationToken cancellationToken = default)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            race.FirstWriterFlushed.TrySetResult(true);
            await race.ReleaseFirstWriter.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class TestPermissionDefinitionManager : IPermissionDefinitionManager
    {
        private readonly IReadOnlyList<PermissionGroupDefinition> _groups;

        public TestPermissionDefinitionManager(IEnumerable<PermissionGroupDefinition> groups)
        {
            _groups = groups.ToArray();
        }

        public IReadOnlyList<PermissionGroupDefinition> Groups => _groups;

        public PermissionGroupDefinition? GetGroupOrNull(string name)
        {
            return _groups.FirstOrDefault(group => string.Equals(group.Name, name, StringComparison.Ordinal));
        }

        public PermissionDefinition? GetOrNull(string name)
        {
            return _groups
                .SelectMany(group => group.Permissions)
                .FirstOrDefault(permission => string.Equals(permission.Name, name, StringComparison.Ordinal));
        }
    }

    private sealed record SeedRunOutcome(bool Succeeded, Exception? Exception);

    private sealed class SeedWriterRace
    {
        public TaskCompletionSource<bool> FirstWriterFlushed { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> ReleaseFirstWriter { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed record SeedState(
        IReadOnlyList<AdministratorState> Administrators,
        IReadOnlyList<RoleState> Roles,
        IReadOnlyList<MembershipState> Memberships,
        IReadOnlyList<GrantState> Grants,
        IReadOnlyList<AuditState> Audits);

    private sealed record AdministratorState(
        Guid Id,
        string UserName,
        string NormalizedUserName,
        string DisplayName,
        string PasswordHash,
        bool IsActive,
        DateTime CreatedAtUtc,
        DateTime? UpdatedAtUtc,
        long Revision);

    private sealed record RoleState(
        Guid Id,
        string Name,
        string NormalizedName,
        bool IsBuiltIn,
        bool IsActive,
        DateTime CreatedAtUtc,
        DateTime? UpdatedAtUtc,
        long Revision);

    private sealed record MembershipState(
        Guid Id,
        Guid AdministratorId,
        Guid RoleId,
        DateTime AssignedAtUtc,
        Guid? AssignedByAdministratorId);

    private sealed record GrantState(
        Guid Id,
        Guid RoleId,
        string Name,
        DateTime GrantedAtUtc,
        Guid? GrantedByAdministratorId);

    private sealed record AuditState(
        Guid Id,
        AdminAccessAuditAction Action,
        DateTime OccurredAtUtc,
        bool Succeeded,
        Guid? AdministratorId,
        Guid? ActorAdministratorId,
        Guid? RoleId,
        string? PermissionName,
        long? TargetRevision);
}
