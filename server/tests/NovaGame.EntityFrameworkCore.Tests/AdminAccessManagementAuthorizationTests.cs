using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;
using NovaCore.BuildingBlocks.CurrentUser;
using NovaCore.BuildingBlocks.Infrastructure;
using NovaCore.BuildingBlocks.Permissions;
using NovaCore.BuildingBlocks.Timing;
using NovaCore.BuildingBlocks.UnitOfWork;
using NovaGame.Application.Auditing;
using NovaGame.Application.Contracts.Operations.AdminAccess;
using NovaGame.Application.Operations.AdminAccess;
using NovaGame.Domain.Operations.AdminAccess;
using NovaGame.Domain.Shared.Operations.AdminAccess;
using Xunit;

namespace NovaGame.EntityFrameworkCore.Tests;

public sealed class AdminAccessManagementAuthorizationTests
{
    public static IEnumerable<object[]> OperationCases()
    {
        yield return [ManagementOperation.ListAdministrators, AdminAccessPermissions.AdministratorsRead];
        yield return [ManagementOperation.CreateAdministrator, AdminAccessPermissions.AdministratorsManage];
        yield return [ManagementOperation.GetAdministrator, AdminAccessPermissions.AdministratorsRead];
        yield return [ManagementOperation.ChangeAdministratorDisplayName, AdminAccessPermissions.AdministratorsManage];
        yield return [ManagementOperation.ReplaceAdministratorRoles, AdminAccessPermissions.AdministratorsManage];
        yield return [ManagementOperation.SetAdministratorActiveState, AdminAccessPermissions.AdministratorsManage];
        yield return [ManagementOperation.ResetAdministratorPassword, AdminAccessPermissions.AdministratorsResetPassword];
        yield return [ManagementOperation.ListRoles, AdminAccessPermissions.RolesRead];
        yield return [ManagementOperation.CreateRole, AdminAccessPermissions.RolesManage];
        yield return [ManagementOperation.GetRole, AdminAccessPermissions.RolesRead];
        yield return [ManagementOperation.RenameRole, AdminAccessPermissions.RolesManage];
        yield return [ManagementOperation.SetRoleActiveState, AdminAccessPermissions.RolesManage];
        yield return [ManagementOperation.ReplaceRolePermissions, AdminAccessPermissions.RolesManage];
        yield return [ManagementOperation.GetPermissionCatalog, AdminAccessPermissions.RolesRead];
        yield return [ManagementOperation.ListSessionFamilies, AdminAccessPermissions.SessionsRead];
        yield return [ManagementOperation.RevokeSessionFamily, AdminAccessPermissions.SessionsRevoke];
        yield return [ManagementOperation.ListAuditRecords, AdminAccessPermissions.AuditLogsRead];
    }

    [Fact]
    public void PermissionCodesMatchTheFrozenNineCodeContract()
    {
        var permissions = new[]
        {
            AdminAccessPermissions.Access,
            AdminAccessPermissions.AdministratorsRead,
            AdminAccessPermissions.AdministratorsManage,
            AdminAccessPermissions.AdministratorsResetPassword,
            AdminAccessPermissions.RolesRead,
            AdminAccessPermissions.RolesManage,
            AdminAccessPermissions.SessionsRead,
            AdminAccessPermissions.SessionsRevoke,
            AdminAccessPermissions.AuditLogsRead
        };

        Assert.Equal(9, permissions.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            new[]
            {
                "Operations.AdminAccess.Access",
                "Operations.AdminAccess.Administrators.Read",
                "Operations.AdminAccess.Administrators.Manage",
                "Operations.AdminAccess.Administrators.ResetPassword",
                "Operations.AdminAccess.Roles.Read",
                "Operations.AdminAccess.Roles.Manage",
                "Operations.AdminAccess.Sessions.Read",
                "Operations.AdminAccess.Sessions.Revoke",
                "Operations.AdminAccess.AuditLogs.Read"
            },
            permissions);
    }

    [Fact]
    public void AdministratorActiveStateJsonRequiresIsActiveAndPreservesFalse()
    {
        AssertRequiredIsActiveJson<SetAdministratorActiveStateRequest>(request => request.IsActive);
    }

    [Fact]
    public void RoleActiveStateJsonRequiresIsActiveAndPreservesFalse()
    {
        AssertRequiredIsActiveJson<SetAdminRoleActiveStateRequest>(request => request.IsActive);
    }

    [Fact]
    public void CreateAdministratorJsonRequiresRoleIdsAndPreservesExplicitEmptyCollection()
    {
        AssertRequiredEmptyCollectionJson<CreateAdministratorRequest, Guid>(
            "{\"roleIds\":[]}",
            request => request.RoleIds);
    }

    [Fact]
    public void ReplaceAdministratorRolesJsonRequiresRoleIdsAndPreservesExplicitEmptyCollection()
    {
        AssertRequiredEmptyCollectionJson<ReplaceAdministratorRolesRequest, Guid>(
            "{\"roleIds\":[]}",
            request => request.RoleIds);
    }

    [Fact]
    public void CreateRoleJsonRequiresPermissionNamesAndPreservesExplicitEmptyCollection()
    {
        AssertRequiredEmptyCollectionJson<CreateAdminRoleRequest, string>(
            "{\"permissionNames\":[]}",
            request => request.PermissionNames);
    }

    [Fact]
    public void ReplaceRolePermissionsJsonRequiresPermissionNamesAndPreservesExplicitEmptyCollection()
    {
        AssertRequiredEmptyCollectionJson<ReplaceAdminRolePermissionsRequest, string>(
            "{\"permissionNames\":[]}",
            request => request.PermissionNames);
    }

    [Theory]
    [MemberData(nameof(OperationCases))]
    public async Task UnauthenticatedUseCasesStopBeforePermissionAndProtectedInput(
        ManagementOperation operation,
        string leafPermission)
    {
        var fixture = new AdminAccessManagementApplicationFixture(
            authenticated: false,
            grantedPermissions: [AdminAccessPermissions.Access, leafPermission]);

        var exception = await Assert.ThrowsAsync<AdminAccessManagementException>(() =>
            InvokeMalformedAsync(fixture.Service, operation));

        Assert.Equal(AdminAccessFailureKind.Unauthorized, exception.FailureKind);
        Assert.Equal(AdminAccessErrorCodes.Unauthorized, exception.Code);
        Assert.Empty(fixture.PermissionChecker.Calls);
        fixture.AssertNoProtectedWork();
    }

    [Theory]
    [MemberData(nameof(OperationCases))]
    public async Task LeafPermissionWithoutAccessStopsBeforeLeafAndProtectedInput(
        ManagementOperation operation,
        string leafPermission)
    {
        var fixture = new AdminAccessManagementApplicationFixture(
            authenticated: true,
            grantedPermissions: [leafPermission]);

        var exception = await Assert.ThrowsAsync<AdminAccessManagementException>(() =>
            InvokeMalformedAsync(fixture.Service, operation));

        Assert.Equal(AdminAccessFailureKind.Forbidden, exception.FailureKind);
        Assert.Equal(AdminAccessErrorCodes.Forbidden, exception.Code);
        Assert.Equal([AdminAccessPermissions.Access], fixture.PermissionChecker.Calls);
        fixture.AssertNoProtectedWork();
    }

    [Theory]
    [MemberData(nameof(OperationCases))]
    public async Task AccessWithoutLeafChecksAccessThenExactLeafAndStopsBeforeProtectedInput(
        ManagementOperation operation,
        string leafPermission)
    {
        var fixture = new AdminAccessManagementApplicationFixture(
            authenticated: true,
            grantedPermissions: [AdminAccessPermissions.Access]);

        var exception = await Assert.ThrowsAsync<AdminAccessManagementException>(() =>
            InvokeMalformedAsync(fixture.Service, operation));

        Assert.Equal(AdminAccessFailureKind.Forbidden, exception.FailureKind);
        Assert.Equal(AdminAccessErrorCodes.Forbidden, exception.Code);
        Assert.Equal(
            [AdminAccessPermissions.Access, leafPermission],
            fixture.PermissionChecker.Calls);
        fixture.AssertNoProtectedWork();
    }

    [Fact]
    public async Task ManagementListQueriesUseExplicitTransactionalRootUnitsOfWork()
    {
        var fixture = new AdminAccessManagementApplicationFixture();

        await fixture.Service.ListAdministratorsAsync(new ListAdministratorsRequest());
        await fixture.Service.ListAdminRolesAsync(new ListAdminRolesRequest());
        await fixture.Service.ListSessionFamiliesAsync(new ListAdminSessionFamiliesRequest());
        await fixture.Service.ListAuditRecordsAsync(new ListAdminAccessAuditRecordsRequest
        {
            FromUtc = AdminAccessManagementApplicationFixture.NowUtc.AddDays(-1),
            ToUtc = AdminAccessManagementApplicationFixture.NowUtc.AddDays(1)
        });

        Assert.Equal(0, fixture.UnitOfWork.BeginCount);
        Assert.Empty(fixture.UnitOfWork.BeginOptions);
        Assert.Equal(4, fixture.Repository.ReadSnapshotExecutionCount);
        Assert.Equal(0, fixture.UnitOfWork.CompleteCount);
        Assert.Equal(0, fixture.UnitOfWork.DisposeCount);
        Assert.Equal(
            ["query-administrators", "query-roles", "query-session-families", "query-audits"],
            fixture.Repository.Calls);
    }

    [Fact]
    public async Task ManagementListQueriesRejectAnAmbientUnitOfWork()
    {
        var fixture = new AdminAccessManagementApplicationFixture();
        await using var ambientScope = await fixture.UnitOfWork.BeginAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.ListAdministratorsAsync(new ListAdministratorsRequest()));

        Assert.Equal(
            "Management list queries require a root transactional unit of work.",
            exception.Message);
        Assert.Equal(1, fixture.UnitOfWork.BeginCount);
        Assert.Equal(0, fixture.Repository.ReadSnapshotExecutionCount);
        Assert.Empty(fixture.Repository.Calls);
    }

    private static async Task InvokeMalformedAsync(
        IAdminAccessManagementAppService service,
        ManagementOperation operation)
    {
        switch (operation)
        {
            case ManagementOperation.ListAdministrators:
                await service.ListAdministratorsAsync(null!);
                break;
            case ManagementOperation.CreateAdministrator:
                await service.CreateAdministratorAsync(null!, Guid.Empty);
                break;
            case ManagementOperation.GetAdministrator:
                await service.GetAdministratorAsync(Guid.Empty);
                break;
            case ManagementOperation.ChangeAdministratorDisplayName:
                await service.ChangeAdministratorDisplayNameAsync(Guid.Empty, 0, null!);
                break;
            case ManagementOperation.ReplaceAdministratorRoles:
                await service.ReplaceAdministratorRolesAsync(Guid.Empty, 0, null!);
                break;
            case ManagementOperation.SetAdministratorActiveState:
                await service.SetAdministratorActiveStateAsync(Guid.Empty, 0, null!);
                break;
            case ManagementOperation.ResetAdministratorPassword:
                await service.ResetAdministratorPasswordAsync(Guid.Empty, 0, null!, Guid.Empty);
                break;
            case ManagementOperation.ListRoles:
                await service.ListAdminRolesAsync(null!);
                break;
            case ManagementOperation.CreateRole:
                await service.CreateAdminRoleAsync(null!, Guid.Empty);
                break;
            case ManagementOperation.GetRole:
                await service.GetAdminRoleAsync(Guid.Empty);
                break;
            case ManagementOperation.RenameRole:
                await service.RenameAdminRoleAsync(Guid.Empty, 0, null!);
                break;
            case ManagementOperation.SetRoleActiveState:
                await service.SetAdminRoleActiveStateAsync(Guid.Empty, 0, null!);
                break;
            case ManagementOperation.ReplaceRolePermissions:
                await service.ReplaceAdminRolePermissionsAsync(Guid.Empty, 0, null!);
                break;
            case ManagementOperation.GetPermissionCatalog:
                await service.GetPermissionCatalogAsync();
                break;
            case ManagementOperation.ListSessionFamilies:
                await service.ListSessionFamiliesAsync(null!);
                break;
            case ManagementOperation.RevokeSessionFamily:
                await service.RevokeSessionFamilyAsync(Guid.Empty);
                break;
            case ManagementOperation.ListAuditRecords:
                await service.ListAuditRecordsAsync(null!);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
        }
    }

    private static void AssertRequiredIsActiveJson<TRequest>(Func<TRequest, bool> getIsActive)
        where TRequest : class
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TRequest>("{}", options));
        var request = JsonSerializer.Deserialize<TRequest>("{\"isActive\":false}", options);

        Assert.NotNull(request);
        Assert.False(getIsActive(request));
    }

    private static void AssertRequiredEmptyCollectionJson<TRequest, TElement>(
        string explicitEmptyJson,
        Func<TRequest, IReadOnlyCollection<TElement>> getCollection)
        where TRequest : class
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TRequest>("{}", options));
        var request = JsonSerializer.Deserialize<TRequest>(explicitEmptyJson, options);

        Assert.NotNull(request);
        Assert.Empty(getCollection(request));
    }
}

public enum ManagementOperation
{
    ListAdministrators,
    CreateAdministrator,
    GetAdministrator,
    ChangeAdministratorDisplayName,
    ReplaceAdministratorRoles,
    SetAdministratorActiveState,
    ResetAdministratorPassword,
    ListRoles,
    CreateRole,
    GetRole,
    RenameRole,
    SetRoleActiveState,
    ReplaceRolePermissions,
    GetPermissionCatalog,
    ListSessionFamilies,
    RevokeSessionFamily,
    ListAuditRecords
}

internal sealed class AdminAccessManagementApplicationFixture
{
    public static readonly Guid ActorId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly DateTime NowUtc = new(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc);

    public AdminAccessManagementApplicationFixture(
        bool authenticated = true,
        IEnumerable<string>? grantedPermissions = null)
    {
        UnitOfWork = new TestUnitOfWorkManager();
        Repository = new TestAdminAccessManagementRepository(UnitOfWork);
        ReceiptRepository = new TestAdminAccessCommandReceiptRepository(UnitOfWork);
        PasswordHasher = new RecordingPasswordHashingService();
        PermissionChecker = new RecordingPermissionChecker(grantedPermissions ?? AllPermissions);
        PermissionDefinitions = new CountingPermissionDefinitionManager(AllPermissions);
        CurrentUser = new TestCurrentUser(authenticated, ActorId, "actor");
        GuidGenerator = new SequentialGuidGenerator();
        Clock = new CountingClock(NowUtc);
        RequestAuditContext = new TestRequestAuditContextAccessor(
            new RequestAuditContext("127.0.0.1", "admin-management-tests", "m35-t02"));

        Service = new AdminAccessManagementAppService(
            UnitOfWork,
            Repository,
            Repository,
            ReceiptRepository,
            new AdminAccessManagementPolicy(),
            new RefreshTokenFamilyPolicy(),
            PasswordHasher,
            PermissionChecker,
            PermissionDefinitions,
            RequestAuditContext,
            CurrentUser,
            GuidGenerator,
            Clock);
    }

    public static IReadOnlyList<string> AllPermissions { get; } =
    [
        AdminAccessPermissions.Access,
        AdminAccessPermissions.AdministratorsRead,
        AdminAccessPermissions.AdministratorsManage,
        AdminAccessPermissions.AdministratorsResetPassword,
        AdminAccessPermissions.RolesRead,
        AdminAccessPermissions.RolesManage,
        AdminAccessPermissions.SessionsRead,
        AdminAccessPermissions.SessionsRevoke,
        AdminAccessPermissions.AuditLogsRead
    ];

    public AdminAccessManagementAppService Service { get; }

    public TestUnitOfWorkManager UnitOfWork { get; }

    public TestAdminAccessManagementRepository Repository { get; }

    public TestAdminAccessCommandReceiptRepository ReceiptRepository { get; }

    public RecordingPasswordHashingService PasswordHasher { get; }

    public RecordingPermissionChecker PermissionChecker { get; }

    public CountingPermissionDefinitionManager PermissionDefinitions { get; }

    public TestCurrentUser CurrentUser { get; }

    public SequentialGuidGenerator GuidGenerator { get; }

    public CountingClock Clock { get; }

    public TestRequestAuditContextAccessor RequestAuditContext { get; }

    public void AssertNoProtectedWork()
    {
        Assert.Equal(0, UnitOfWork.BeginCount);
        Assert.Empty(Repository.Calls);
        Assert.Equal(0, ReceiptRepository.ClaimCount);
        Assert.Empty(PasswordHasher.HashedPasswords);
        Assert.Equal(0, GuidGenerator.CreateCount);
        Assert.Equal(0, Clock.UtcNowReadCount);
        Assert.Equal(0, PermissionDefinitions.AccessCount);
    }
}

internal sealed class TestCurrentUser : ICurrentUser
{
    public TestCurrentUser(bool isAuthenticated, Guid id, string userName)
    {
        IsAuthenticated = isAuthenticated;
        Id = isAuthenticated ? id.ToString("D") : null;
        UserName = isAuthenticated ? userName : null;
    }

    public string? Id { get; }

    public string? UserName { get; }

    public ClaimsPrincipal? Principal => null;

    public bool IsAuthenticated { get; }
}

internal sealed class RecordingPermissionChecker : IPermissionChecker
{
    private readonly HashSet<string> _grantedPermissions;

    public RecordingPermissionChecker(IEnumerable<string> grantedPermissions)
    {
        _grantedPermissions = grantedPermissions.ToHashSet(StringComparer.Ordinal);
    }

    public List<string> Calls { get; } = [];

    public Task<bool> IsGrantedAsync(
        string permissionName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (Calls)
        {
            Calls.Add(permissionName);
        }

        return Task.FromResult(_grantedPermissions.Contains(permissionName));
    }
}

internal sealed class CountingPermissionDefinitionManager : IPermissionDefinitionManager
{
    private readonly IReadOnlyList<PermissionGroupDefinition> _groups;
    private readonly IReadOnlyDictionary<string, PermissionDefinition> _permissions;
    private int _accessCount;

    public CountingPermissionDefinitionManager(IEnumerable<string> permissionNames)
    {
        var group = new PermissionGroupDefinition(AdminAccessPermissions.GroupName, "Administrator access");
        foreach (var permissionName in permissionNames.Order(StringComparer.Ordinal))
        {
            group.AddPermission(permissionName, permissionName, $"Description for {permissionName}");
        }

        _groups = [group];
        _permissions = group.Permissions.ToDictionary(permission => permission.Name, StringComparer.Ordinal);
    }

    public int AccessCount => Volatile.Read(ref _accessCount);

    public IReadOnlyList<PermissionGroupDefinition> Groups
    {
        get
        {
            Interlocked.Increment(ref _accessCount);
            return _groups;
        }
    }

    public PermissionGroupDefinition? GetGroupOrNull(string name)
    {
        Interlocked.Increment(ref _accessCount);
        return _groups.SingleOrDefault(group => string.Equals(group.Name, name, StringComparison.Ordinal));
    }

    public PermissionDefinition? GetOrNull(string name)
    {
        Interlocked.Increment(ref _accessCount);
        return _permissions.GetValueOrDefault(name);
    }
}

internal sealed class CountingClock : IClock
{
    private readonly DateTime _utcNow;
    private int _utcNowReadCount;

    public CountingClock(DateTime utcNow)
    {
        _utcNow = utcNow;
    }

    public int UtcNowReadCount => Volatile.Read(ref _utcNowReadCount);

    public DateTime Now => UtcNow.ToLocalTime();

    public DateTime UtcNow
    {
        get
        {
            Interlocked.Increment(ref _utcNowReadCount);
            return _utcNow;
        }
    }
}

internal sealed class SequentialGuidGenerator : IGuidGenerator
{
    private int _createCount;

    public int CreateCount => Volatile.Read(ref _createCount);

    public Guid Create()
    {
        var value = Interlocked.Increment(ref _createCount);
        return Guid.Parse($"90000000-0000-0000-0000-{value:x12}");
    }
}

internal sealed class RecordingPasswordHashingService : IPasswordHashingService
{
    public List<string> HashedPasswords { get; } = [];

    public string Hash(string password)
    {
        lock (HashedPasswords)
        {
            HashedPasswords.Add(password);
        }

        return $"hashed::{password}";
    }

    public PasswordHashVerificationResult Verify(string? passwordHash, string password)
    {
        return string.Equals(passwordHash, $"hashed::{password}", StringComparison.Ordinal)
            ? PasswordHashVerificationResult.Success
            : PasswordHashVerificationResult.Failed;
    }
}

internal sealed class TestRequestAuditContextAccessor : IRequestAuditContextAccessor
{
    public TestRequestAuditContextAccessor(RequestAuditContext current)
    {
        Current = current;
    }

    public RequestAuditContext Current { get; }
}

internal sealed class TestUnitOfWorkManager : IUnitOfWorkManager
{
    private readonly AsyncLocal<TestUnitOfWorkScope?> _current = new();
    private readonly ConcurrentQueue<UnitOfWorkOptions> _beginOptions = new();
    private int _beginCount;
    private int _completeAttemptCount;
    private int _completeCount;
    private int _disposeCount;

    public int BeginCount => Volatile.Read(ref _beginCount);

    public int CompleteAttemptCount => Volatile.Read(ref _completeAttemptCount);

    public int CompleteCount => Volatile.Read(ref _completeCount);

    public int DisposeCount => Volatile.Read(ref _disposeCount);

    public IReadOnlyList<UnitOfWorkOptions> BeginOptions => _beginOptions.ToArray();

    public Exception? CompleteFailure { get; set; }

    public TestUnitOfWorkScope CurrentScope =>
        _current.Value ?? throw new InvalidOperationException("A test unit of work is required.");

    public IUnitOfWork? Current => _current.Value?.UnitOfWork;

    public Task<IUnitOfWorkScope> BeginAsync(
        UnitOfWorkOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_current.Value is not null)
        {
            throw new InvalidOperationException("Nested test units of work are not supported.");
        }

        var resolvedOptions = options ?? new UnitOfWorkOptions();
        _beginOptions.Enqueue(resolvedOptions);
        Interlocked.Increment(ref _beginCount);
        var scope = new TestUnitOfWorkScope(
            this,
            resolvedOptions);
        _current.Value = scope;
        return Task.FromResult<IUnitOfWorkScope>(scope);
    }

    internal void OnCompleted()
    {
        Interlocked.Increment(ref _completeCount);
    }

    internal void OnCompleteAttempt()
    {
        Interlocked.Increment(ref _completeAttemptCount);
    }

    internal void OnDisposed(TestUnitOfWorkScope scope)
    {
        Interlocked.Increment(ref _disposeCount);
        if (ReferenceEquals(_current.Value, scope))
        {
            _current.Value = null;
        }
    }
}

internal sealed class TestUnitOfWorkScope : IUnitOfWorkScope
{
    private readonly TestUnitOfWorkManager _owner;
    private readonly List<Action> _commitActions = [];
    private readonly List<Action> _disposeActions = [];
    private bool _completed;
    private bool _disposed;

    public TestUnitOfWorkScope(TestUnitOfWorkManager owner, UnitOfWorkOptions options)
    {
        _owner = owner;
        Options = options;
        UnitOfWork = new TestUnitOfWork(options);
    }

    public IUnitOfWork UnitOfWork { get; }

    public UnitOfWorkOptions Options { get; }

    public bool IsRoot => true;

    public void RegisterCommit(Action action)
    {
        _commitActions.Add(action);
    }

    public void RegisterDispose(Action action)
    {
        _disposeActions.Add(action);
    }

    public Task<int> CompleteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_completed)
        {
            throw new InvalidOperationException("The test unit of work was already completed.");
        }

        _owner.OnCompleteAttempt();
        if (_owner.CompleteFailure is { } failure)
        {
            return Task.FromException<int>(failure);
        }

        foreach (var action in _commitActions)
        {
            action();
        }

        _completed = true;
        _owner.OnCompleted();
        return Task.FromResult(0);
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        for (var index = _disposeActions.Count - 1; index >= 0; index--)
        {
            _disposeActions[index]();
        }

        _owner.OnDisposed(this);
        return ValueTask.CompletedTask;
    }
}

internal sealed class TestUnitOfWork : IUnitOfWork
{
    public TestUnitOfWork(UnitOfWorkOptions options)
    {
        Options = options;
    }

    public UnitOfWorkOptions Options { get; }

    public bool IsActive { get; private set; } = true;

    public bool IsCompleted { get; private set; }

    public Task BeginAsync(UnitOfWorkOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(0);
    }

    public Task<int> CompleteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsCompleted = true;
        IsActive = false;
        return Task.FromResult(0);
    }

    public ValueTask DisposeAsync()
    {
        IsActive = false;
        return ValueTask.CompletedTask;
    }
}

internal sealed class TestAdminAccessCommandReceiptRepository : IAdminAccessCommandReceiptRepository
{
    private readonly TestUnitOfWorkManager _unitOfWork;
    private readonly ConcurrentDictionary<ReceiptKey, AdminAccessCommandReceipt> _committed = new();
    private readonly ConcurrentDictionary<ReceiptKey, SemaphoreSlim> _locks = new();
    private int _claimCount;

    public TestAdminAccessCommandReceiptRepository(TestUnitOfWorkManager unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public int ClaimCount => Volatile.Read(ref _claimCount);

    public int CommittedCount => _committed.Count;

    public TaskCompletionSource SecondAttempted { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<AdminAccessCommandReceipt> ClaimAsync(
        AdminAccessCommandReceipt candidate,
        CancellationToken cancellationToken = default)
    {
        var attempt = Interlocked.Increment(ref _claimCount);
        if (attempt >= 2)
        {
            SecondAttempted.TrySetResult();
        }

        var key = new ReceiptKey(
            candidate.ActorAdministratorId,
            candidate.CommandKind,
            candidate.IdempotencyKey);
        var semaphore = _locks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        _unitOfWork.CurrentScope.RegisterDispose(() => semaphore.Release());

        if (_committed.TryGetValue(key, out var existing))
        {
            return existing;
        }

        _unitOfWork.CurrentScope.RegisterCommit(() =>
        {
            if (!_committed.TryAdd(key, candidate))
            {
                throw new InvalidOperationException("A receipt was committed twice in the test store.");
            }
        });
        return candidate;
    }

    private readonly record struct ReceiptKey(
        Guid ActorAdministratorId,
        AdminAccessCommandKind CommandKind,
        Guid IdempotencyKey);
}

internal sealed class TestAdminAccessManagementRepository :
    IAdminAccessManagementRepository,
    IAdminAccessManagementReadSnapshot
{
    private readonly TestUnitOfWorkManager _unitOfWork;
    private readonly object _sync = new();

    public TestAdminAccessManagementRepository(TestUnitOfWorkManager unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public List<string> Calls { get; } = [];

    public Dictionary<Guid, Administrator> Administrators { get; } = [];

    public Dictionary<Guid, AdminRole> Roles { get; } = [];

    public List<RefreshTokenSession> Sessions { get; } = [];

    public List<AdminAccessAuditRecord> Audits { get; } = [];

    public List<Guid> RoleLockOrder { get; } = [];

    public List<bool> RoleNameProbeHasAmbientUnitOfWork { get; } = [];

    public List<Guid> SessionFamilyLockOrder { get; } = [];

    public Exception? AuditFailure { get; set; }

    public int ReadSnapshotExecutionCount => Volatile.Read(ref _readSnapshotExecutionCount);

    public bool UseDetachedRoleUpdates { get; set; }

    private int _readSnapshotExecutionCount;

    public Func<Administrator, CancellationToken, Task>? OnAddAdministratorAsync { get; set; }

    public Action<IReadOnlyList<AdminRole>>? OnRolesLocked { get; set; }

    public void SeedAdministrator(Administrator administrator)
    {
        Administrators.Add(administrator.Id, administrator);
    }

    public void SeedRole(AdminRole role)
    {
        Roles.Add(role.Id, role);
    }

    public void SeedSession(RefreshTokenSession session)
    {
        Sessions.Add(session);
    }

    public async Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _readSnapshotExecutionCount);
        return await operation(cancellationToken);
    }

    public Task<AdminAccessPageResult<AdministratorManagementSnapshot>> GetAdministratorPageAsync(
        string? normalizedUserNamePrefix,
        bool? isActive,
        Guid? roleId,
        int skipCount,
        int maxResultCount,
        CancellationToken cancellationToken = default)
    {
        Record("query-administrators");
        var query = Administrators.Values.AsEnumerable();
        if (normalizedUserNamePrefix is not null)
        {
            query = query.Where(item => item.NormalizedUserName.StartsWith(
                normalizedUserNamePrefix,
                StringComparison.Ordinal));
        }

        if (isActive.HasValue)
        {
            query = query.Where(item => item.IsActive == isActive.Value);
        }

        if (roleId.HasValue)
        {
            query = query.Where(item => item.RoleMemberships.Any(role => role.RoleId == roleId.Value));
        }

        var ordered = query.OrderBy(item => item.NormalizedUserName, StringComparer.Ordinal)
            .ThenBy(item => item.Id)
            .ToArray();
        return Task.FromResult(new AdminAccessPageResult<AdministratorManagementSnapshot>(
            ordered.Skip(skipCount).Take(maxResultCount).Select(CreateAdministratorSnapshot).ToArray(),
            ordered.LongLength));
    }

    public Task<AdministratorManagementSnapshot?> FindAdministratorSnapshotAsync(
        Guid administratorId,
        CancellationToken cancellationToken = default)
    {
        Record("query-administrator-snapshot");
        return Task.FromResult(
            Administrators.TryGetValue(administratorId, out var administrator)
                ? CreateAdministratorSnapshot(administrator)
                : null);
    }

    public Task<AdminAccessPageResult<AdminRoleManagementSnapshot>> GetRolePageAsync(
        string? normalizedNamePrefix,
        bool? isActive,
        bool? isBuiltIn,
        IReadOnlyCollection<string> registeredPermissionNames,
        int skipCount,
        int maxResultCount,
        CancellationToken cancellationToken = default)
    {
        Record("query-roles");
        var query = Roles.Values.AsEnumerable();
        if (normalizedNamePrefix is not null)
        {
            query = query.Where(item => item.NormalizedName.StartsWith(
                normalizedNamePrefix,
                StringComparison.Ordinal));
        }

        if (isActive.HasValue)
        {
            query = query.Where(item => item.IsActive == isActive.Value);
        }

        if (isBuiltIn.HasValue)
        {
            query = query.Where(item => item.IsBuiltIn == isBuiltIn.Value);
        }

        var ordered = query.OrderByDescending(item => item.IsBuiltIn)
            .ThenBy(item => item.NormalizedName, StringComparer.Ordinal)
            .ThenBy(item => item.Id)
            .ToArray();
        return Task.FromResult(new AdminAccessPageResult<AdminRoleManagementSnapshot>(
            ordered.Skip(skipCount).Take(maxResultCount)
                .Select(role => CreateRoleSnapshot(role, registeredPermissionNames))
                .ToArray(),
            ordered.LongLength));
    }

    public Task<AdminRoleManagementSnapshot?> FindRoleSnapshotAsync(
        Guid roleId,
        IReadOnlyCollection<string> registeredPermissionNames,
        CancellationToken cancellationToken = default)
    {
        Record("query-role-snapshot");
        return Task.FromResult(
            Roles.TryGetValue(roleId, out var role)
                ? CreateRoleSnapshot(role, registeredPermissionNames)
                : null);
    }

    public Task<AdminAccessPageResult<AdminSessionFamilySnapshot>> GetSessionFamilyPageAsync(
        Guid? administratorId,
        AdminSessionFamilyStateSnapshot? state,
        DateTime nowUtc,
        int skipCount,
        int maxResultCount,
        CancellationToken cancellationToken = default)
    {
        Record("query-session-families");
        return Task.FromResult(new AdminAccessPageResult<AdminSessionFamilySnapshot>([], 0));
    }

    public Task<AdminAccessPageResult<AdminAccessAuditRecordSnapshot>> GetAuditPageAsync(
        AdminAccessAuditQuery query,
        int skipCount,
        int maxResultCount,
        CancellationToken cancellationToken = default)
    {
        Record("query-audits");
        var items = Audits
            .OrderByDescending(item => item.OccurredAtUtc)
            .ThenByDescending(item => item.Id)
            .Skip(skipCount)
            .Take(maxResultCount)
            .Select(item => new AdminAccessAuditRecordSnapshot(
                item.Id,
                item.Action,
                item.OccurredAtUtc,
                item.Succeeded,
                item.AdministratorId,
                item.ActorAdministratorId,
                item.RoleId,
                item.SessionFamilyId,
                item.SubjectUserName,
                item.PermissionName,
                item.ErrorCode,
                item.IpAddress,
                item.UserAgent,
                item.CorrelationId,
                item.TargetRevision))
            .ToArray();
        return Task.FromResult(new AdminAccessPageResult<AdminAccessAuditRecordSnapshot>(items, Audits.Count));
    }

    public Task<IReadOnlyList<AdminRole>> GetBuiltInRolesForUpdateAsync(
        CancellationToken cancellationToken = default)
    {
        Record("lock-built-in-role");
        return Task.FromResult<IReadOnlyList<AdminRole>>(
            Roles.Values.Where(role => role.IsBuiltIn).OrderBy(role => role.Id).ToArray());
    }

    public Task<IReadOnlyList<Guid>> GetAdministratorRoleIdsAsync(
        Guid administratorId,
        CancellationToken cancellationToken = default)
    {
        Record("probe-memberships");
        return Task.FromResult<IReadOnlyList<Guid>>(
            Administrators.TryGetValue(administratorId, out var administrator)
                ? administrator.RoleMemberships.Select(item => item.RoleId).Order().ToArray()
                : []);
    }

    public Task<IReadOnlyList<AdminRole>> GetRolesAsync(
        IReadOnlyCollection<Guid> roleIds,
        CancellationToken cancellationToken = default)
    {
        Record("read-roles");
        return Task.FromResult<IReadOnlyList<AdminRole>>(
            roleIds.Order().Where(Roles.ContainsKey).Select(id => Roles[id]).ToArray());
    }

    public Task<IReadOnlyList<AdminRole>> GetRolesForUpdateInGlobalOrderAsync(
        IReadOnlyCollection<Guid> roleIds,
        CancellationToken cancellationToken = default)
    {
        Record("lock-roles");
        var roles = roleIds
            .Where(Roles.ContainsKey)
            .Select(roleId => Roles[roleId])
            .OrderByDescending(role => role.IsBuiltIn)
            .ThenBy(role => role.Id)
            .ToArray();
        lock (RoleLockOrder)
        {
            RoleLockOrder.AddRange(roles.Select(role => role.Id));
        }

        OnRolesLocked?.Invoke(roles);

        return Task.FromResult<IReadOnlyList<AdminRole>>(roles);
    }

    public Task<AdminRole?> FindRoleForUpdateAsync(
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        Record("lock-role");
        var role = Roles.GetValueOrDefault(roleId);
        if (role is null || !UseDetachedRoleUpdates)
        {
            return Task.FromResult(role);
        }

        if (role.Revision != 1 || role.UpdatedAtUtc is not null || role.Permissions.Count != 0)
        {
            throw new InvalidOperationException(
                "Detached test role updates require a newly created custom role without grants.");
        }

        var detachedRole = new AdminRole(
            role.Id,
            role.Name,
            role.CreatedAtUtc,
            role.IsBuiltIn);
        _unitOfWork.CurrentScope.RegisterCommit(() =>
        {
            lock (_sync)
            {
                Roles[roleId] = detachedRole;
            }
        });
        return Task.FromResult<AdminRole?>(detachedRole);
    }

    public Task<IReadOnlyList<RolePermission>> GetRolePermissionsForUpdateAsync(
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        Record("lock-role-permissions");
        return Task.FromResult<IReadOnlyList<RolePermission>>(
            Roles.TryGetValue(roleId, out var role)
                ? role.Permissions.OrderBy(item => item.Name, StringComparer.Ordinal).ThenBy(item => item.Id).ToArray()
                : []);
    }

    public Task<AdminRole?> FindRoleByNormalizedNameForUpdateAsync(
        string normalizedName,
        CancellationToken cancellationToken = default)
    {
        Record("lock-role-name");
        return Task.FromResult(Roles.Values.SingleOrDefault(
                role => string.Equals(role.NormalizedName, normalizedName, StringComparison.Ordinal)));
    }

    public Task<AdminRole?> FindRoleByNormalizedNameAsync(
        string normalizedName,
        CancellationToken cancellationToken = default)
    {
        Record("probe-role-name");
        lock (RoleNameProbeHasAmbientUnitOfWork)
        {
            RoleNameProbeHasAmbientUnitOfWork.Add(_unitOfWork.Current is not null);
        }

        return Task.FromResult(Roles.Values.SingleOrDefault(
            role => string.Equals(role.NormalizedName, normalizedName, StringComparison.Ordinal)));
    }

    public Task<Administrator?> FindAdministratorForUpdateAsync(
        Guid administratorId,
        CancellationToken cancellationToken = default)
    {
        Record("lock-administrator");
        return Task.FromResult(Administrators.GetValueOrDefault(administratorId));
    }

    public Task<IReadOnlyList<AdministratorRole>> GetAdministratorRoleMembershipsForUpdateAsync(
        Guid administratorId,
        CancellationToken cancellationToken = default)
    {
        Record("lock-memberships");
        return Task.FromResult<IReadOnlyList<AdministratorRole>>(
            Administrators.TryGetValue(administratorId, out var administrator)
                ? administrator.RoleMemberships
                    .OrderBy(item => item.AdministratorId)
                    .ThenBy(item => item.RoleId)
                    .ThenBy(item => item.Id)
                    .ToArray()
                : []);
    }

    public Task<Administrator?> FindAdministratorByNormalizedUserNameForUpdateAsync(
        string normalizedUserName,
        CancellationToken cancellationToken = default)
    {
        Record("lock-administrator-name");
        return Task.FromResult(Administrators.Values.SingleOrDefault(
            administrator => string.Equals(
                administrator.NormalizedUserName,
                normalizedUserName,
                StringComparison.Ordinal)));
    }

    public Task<int> CountActiveAdministratorsInRoleAsync(
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        Record("count-active-role-members");
        return Task.FromResult(Administrators.Values.Count(
            administrator => administrator.IsActive &&
                administrator.RoleMemberships.Any(item => item.RoleId == roleId)));
    }

    public Task<long> CountAdministratorsInRoleAsync(
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        Record("count-role-members");
        return Task.FromResult(Administrators.Values.LongCount(
            administrator => administrator.RoleMemberships.Any(item => item.RoleId == roleId)));
    }

    public Task<IReadOnlyList<Guid>> GetAdministratorSessionFamilyIdsAsync(
        Guid administratorId,
        CancellationToken cancellationToken = default)
    {
        Record("probe-administrator-session-families");
        return Task.FromResult<IReadOnlyList<Guid>>(
            Sessions.Where(item => item.AdministratorId == administratorId)
                .Select(item => item.FamilyId)
                .Distinct()
                .Order()
                .ToArray());
    }

    public Task<IReadOnlyList<Guid>> GetSessionFamilyOwnerIdsAsync(
        Guid familyId,
        CancellationToken cancellationToken = default)
    {
        Record("probe-family-owners");
        var owners = Sessions.Where(item => item.FamilyId == familyId)
            .Select(item => item.AdministratorId)
            .Distinct()
            .Order()
            .ToArray();
        return Task.FromResult<IReadOnlyList<Guid>>(owners);
    }

    public Task<IReadOnlyList<RefreshTokenSession>> GetSessionFamilyForUpdateAsync(
        Guid familyId,
        CancellationToken cancellationToken = default)
    {
        Record("lock-family");
        lock (SessionFamilyLockOrder)
        {
            SessionFamilyLockOrder.Add(familyId);
        }

        var sessions = Sessions.Where(item => item.FamilyId == familyId)
            .OrderBy(item => item.CreatedAtUtc)
            .ThenBy(item => item.Id)
            .ToArray();
        return Task.FromResult<IReadOnlyList<RefreshTokenSession>>(sessions);
    }

    public async Task AddAdministratorAsync(
        Administrator administrator,
        CancellationToken cancellationToken = default)
    {
        Record("add-administrator");
        _unitOfWork.CurrentScope.RegisterCommit(() =>
        {
            lock (_sync)
            {
                Administrators.Add(administrator.Id, administrator);
            }
        });
        if (OnAddAdministratorAsync is not null)
        {
            await OnAddAdministratorAsync(administrator, cancellationToken);
        }
    }

    public Task AddRoleAsync(AdminRole role, CancellationToken cancellationToken = default)
    {
        Record("add-role");
        _unitOfWork.CurrentScope.RegisterCommit(() =>
        {
            lock (_sync)
            {
                Roles.Add(role.Id, role);
            }
        });
        return Task.CompletedTask;
    }

    public Task AddAuditAsync(
        AdminAccessAuditRecord auditRecord,
        CancellationToken cancellationToken = default)
    {
        Record("add-audit");
        if (AuditFailure is not null)
        {
            return Task.FromException(AuditFailure);
        }

        _unitOfWork.CurrentScope.RegisterCommit(() =>
        {
            lock (_sync)
            {
                Audits.Add(auditRecord);
            }
        });
        return Task.CompletedTask;
    }

    private AdministratorManagementSnapshot CreateAdministratorSnapshot(Administrator administrator)
    {
        var roles = administrator.RoleMemberships
            .Select(membership => Roles.GetValueOrDefault(membership.RoleId))
            .Where(role => role is not null)
            .Select(role => new AdminRoleReferenceSnapshot(
                role!.Id,
                role.Name,
                role.IsBuiltIn,
                role.IsActive))
            .OrderBy(role => role.Name, StringComparer.Ordinal)
            .ThenBy(role => role.Id)
            .ToArray();
        return new AdministratorManagementSnapshot(
            administrator.Id,
            administrator.UserName,
            administrator.DisplayName,
            administrator.IsActive,
            roles,
            administrator.CreatedAtUtc,
            administrator.UpdatedAtUtc,
            administrator.LastLoginAtUtc,
            administrator.Revision);
    }

    private AdminRoleManagementSnapshot CreateRoleSnapshot(
        AdminRole role,
        IReadOnlyCollection<string> registeredPermissionNames)
    {
        var registered = registeredPermissionNames.ToHashSet(StringComparer.Ordinal);
        return new AdminRoleManagementSnapshot(
            role.Id,
            role.Name,
            role.IsBuiltIn,
            role.IsActive,
            role.Permissions.Select(permission => permission.Name)
                .Where(registered.Contains)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            Administrators.Values.LongCount(administrator =>
                administrator.RoleMemberships.Any(item => item.RoleId == role.Id)),
            role.CreatedAtUtc,
            role.UpdatedAtUtc,
            role.Revision);
    }

    private void Record(string call)
    {
        lock (Calls)
        {
            Calls.Add(call);
        }
    }
}
