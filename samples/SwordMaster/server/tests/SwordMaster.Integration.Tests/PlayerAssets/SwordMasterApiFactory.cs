using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NovaCore.BuildingBlocks.Permissions;
using NovaGame.Api;
using SwordMaster.Api;
using SwordMaster.Application.Contracts.PlayerAssets;
using SwordMaster.Application.PlayerAssets;

namespace SwordMaster.Integration.Tests.PlayerAssets;

internal sealed class SwordMasterApiFactory : WebApplicationFactory<NovaGameApiModule>
{
    internal const string AdminIssuer = "NovaGame.SwordMaster.Tests";
    internal const string AdminAudience = "NovaGame.SwordMaster.Admin.Tests";
    internal const string AdminSigningKey = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY";
    internal const string PlayerIssuer = "SwordMaster.Tests";
    internal const string PlayerAudience = "SwordMaster.Player.Tests";
    internal const string PlayerSigningKey = "YWJjZGVmMDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODk";

    private readonly Action<IServiceCollection>? _configureTestServices;

    public SwordMasterApiFactory(
        Action<IServiceCollection>? configureTestServices = null)
    {
        _configureTestServices = configureTestServices;
    }

    public PlayerAssetsApiProbe Probe { get; } = new();

    public PlayerEquipmentGrantApiProbe EquipmentGrantProbe { get; } = new();

    public ConfigurableEquipmentGrantPermissionChecker EquipmentGrantPermissionChecker { get; } =
        new();

    public HttpClient CreateHttpsClient()
    {
        return CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false
        });
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting(
            "ConnectionStrings:Default",
            "Server=127.0.0.1;Port=1;Database=novagame_api_tests;User ID=unused;Password=unused;");
        builder.UseSetting("NovaGame:EntityFrameworkCore:ServerVersion", "8.4.0");
        builder.UseSetting("NovaGame:AdminAccess:Jwt:Issuer", AdminIssuer);
        builder.UseSetting("NovaGame:AdminAccess:Jwt:Audience", AdminAudience);
        builder.UseSetting("NovaGame:AdminAccess:Jwt:SigningKey", AdminSigningKey);
        builder.UseSetting("NovaGame:AdminAccess:Jwt:ClockSkew", "00:00:00");
        builder.UseSetting("SwordMaster:PlayerAuthentication:Jwt:Issuer", PlayerIssuer);
        builder.UseSetting("SwordMaster:PlayerAuthentication:Jwt:Audience", PlayerAudience);
        builder.UseSetting("SwordMaster:PlayerAuthentication:Jwt:SigningKey", PlayerSigningKey);
        builder.UseSetting("SwordMaster:PlayerAuthentication:Jwt:ClockSkew", "00:00:00");

        builder.ConfigureServices((context, services) =>
        {
            new SwordMasterApiModule().ConfigureServices(services, context.Configuration);
            services.TryAddEnumerable(
                ServiceDescriptor.Transient<
                    IPermissionDefinitionProvider,
                    PlayerEquipmentGrantPermissionDefinitionProvider>());
            services.RemoveAll<IPermissionChecker>();
            services.AddSingleton<IPermissionChecker>(EquipmentGrantPermissionChecker);
            services.RemoveAll<IPlayerAssetsAppService>();
            services.AddSingleton(Probe);
            services.AddScoped<IPlayerAssetsAppService, CapturingPlayerAssetsAppService>();
            services.RemoveAll<IPlayerEquipmentGrantAppService>();
            services.AddSingleton(EquipmentGrantProbe);
            services.AddScoped<
                IPlayerEquipmentGrantAppService,
                CapturingPlayerEquipmentGrantAppService>();
            _configureTestServices?.Invoke(services);
        });
    }
}

internal sealed class ConfigurableEquipmentGrantPermissionChecker : IPermissionChecker
{
    public bool IsGranted { get; set; } = true;

    public List<string> PermissionChecks { get; } = [];

    public Task<bool> IsGrantedAsync(
        string permissionName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PermissionChecks.Add(permissionName);
        return Task.FromResult(
            IsGranted &&
            string.Equals(
                permissionName,
                PlayerEquipmentGrantPermissions.Manage,
                StringComparison.Ordinal));
    }
}

internal sealed class PlayerEquipmentGrantApiProbe
{
    private static readonly Guid DefaultGrantId =
        Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid DefaultPlayerId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DefaultOwnedItemId =
        Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid DefaultConfigurationVersionId =
        Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid DefaultAdministratorId =
        Guid.Parse("88888888-8888-8888-8888-888888888888");
    private static readonly DateTime DefaultGrantedAtUtc =
        new(2026, 8, 10, 1, 2, 3, DateTimeKind.Utc);
    private static readonly DateTime DefaultRolledBackAtUtc =
        new(2026, 8, 10, 2, 3, 4, DateTimeKind.Utc);

    public PlayerEquipmentGrantPageDto PageResult { get; set; } = new(
        DefaultPlayerId,
        Revision: 7,
        Page: 1,
        PageSize: 20,
        TotalCount: 1,
        Items:
        [
            new PlayerEquipmentGrantDto(
                DefaultGrantId,
                DefaultPlayerId,
                DefaultOwnedItemId,
                "starter-sword",
                DefaultConfigurationVersionId,
                PlayerEquipmentGrantStatusDto.Active,
                GrantResultingRevision: 7,
                DefaultAdministratorId,
                DefaultGrantedAtUtc,
                "Customer support correction",
                RollbackResultingRevision: null,
                RolledBackByAdministratorId: null,
                RolledBackAtUtc: null,
                RollbackReason: null)
        ]);

    public PlayerEquipmentGrantMutationResultDto GrantResult { get; set; } = new(
        DefaultGrantId,
        DefaultPlayerId,
        DefaultOwnedItemId,
        "starter-sword",
        DefaultConfigurationVersionId,
        PlayerEquipmentGrantStatusDto.Active,
        ResultingRevision: 8,
        DefaultAdministratorId,
        DefaultGrantedAtUtc,
        "Customer support correction",
        RolledBackByAdministratorId: null,
        RolledBackAtUtc: null,
        RollbackReason: null);

    public PlayerEquipmentGrantMutationResultDto RollbackResult { get; set; } = new(
        DefaultGrantId,
        DefaultPlayerId,
        DefaultOwnedItemId,
        "starter-sword",
        DefaultConfigurationVersionId,
        PlayerEquipmentGrantStatusDto.RolledBack,
        ResultingRevision: 9,
        DefaultAdministratorId,
        DefaultGrantedAtUtc,
        "Customer support correction",
        DefaultAdministratorId,
        DefaultRolledBackAtUtc,
        "Correction approved by support lead");

    public Exception? ExceptionToThrow { get; set; }

    public List<GetEquipmentGrantPageApiCall> GetPageCalls { get; } = [];

    public List<GrantEquipmentApiCall> GrantCalls { get; } = [];

    public List<RollbackEquipmentGrantApiCall> RollbackCalls { get; } = [];

    public int CallCount => GetPageCalls.Count + GrantCalls.Count + RollbackCalls.Count;

    public void ThrowIfConfigured()
    {
        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }
    }
}

internal sealed record EquipmentGrantHttpContextSnapshot(
    bool IsAdministratorAuthenticated,
    Guid? AdministratorId,
    PlayerEquipmentGrantAuditContext AuditContext);

internal sealed record GetEquipmentGrantPageApiCall(
    Guid PlayerId,
    int Page,
    int PageSize,
    EquipmentGrantHttpContextSnapshot HttpContext);

internal sealed record GrantEquipmentApiCall(
    Guid PlayerId,
    GrantPlayerEquipmentRequest Request,
    PlayerEquipmentGrantMutationContext MutationContext,
    EquipmentGrantHttpContextSnapshot HttpContext);

internal sealed record RollbackEquipmentGrantApiCall(
    Guid PlayerId,
    Guid GrantId,
    RollbackPlayerEquipmentGrantRequest Request,
    PlayerEquipmentGrantMutationContext MutationContext,
    EquipmentGrantHttpContextSnapshot HttpContext);

internal sealed class CapturingPlayerEquipmentGrantAppService :
    IPlayerEquipmentGrantAppService
{
    private readonly ICurrentSwordMasterAdministrator _currentAdministrator;
    private readonly IPlayerEquipmentGrantAuditContextAccessor _auditContextAccessor;
    private readonly PlayerEquipmentGrantApiProbe _probe;

    public CapturingPlayerEquipmentGrantAppService(
        ICurrentSwordMasterAdministrator currentAdministrator,
        IPlayerEquipmentGrantAuditContextAccessor auditContextAccessor,
        PlayerEquipmentGrantApiProbe probe)
    {
        _currentAdministrator = currentAdministrator;
        _auditContextAccessor = auditContextAccessor;
        _probe = probe;
    }

    public Task<PlayerEquipmentGrantPageDto> GetPageAsync(
        Guid playerId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _probe.GetPageCalls.Add(new GetEquipmentGrantPageApiCall(
            playerId,
            page,
            pageSize,
            CaptureHttpContext()));
        _probe.ThrowIfConfigured();
        return Task.FromResult(_probe.PageResult);
    }

    public Task<PlayerEquipmentGrantMutationResultDto> GrantAsync(
        Guid playerId,
        GrantPlayerEquipmentRequest request,
        PlayerEquipmentGrantMutationContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _probe.GrantCalls.Add(new GrantEquipmentApiCall(
            playerId,
            request,
            context,
            CaptureHttpContext()));
        _probe.ThrowIfConfigured();
        return Task.FromResult(_probe.GrantResult);
    }

    public Task<PlayerEquipmentGrantMutationResultDto> RollbackAsync(
        Guid playerId,
        Guid grantId,
        RollbackPlayerEquipmentGrantRequest request,
        PlayerEquipmentGrantMutationContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _probe.RollbackCalls.Add(new RollbackEquipmentGrantApiCall(
            playerId,
            grantId,
            request,
            context,
            CaptureHttpContext()));
        _probe.ThrowIfConfigured();
        return Task.FromResult(_probe.RollbackResult);
    }

    private EquipmentGrantHttpContextSnapshot CaptureHttpContext()
    {
        return new EquipmentGrantHttpContextSnapshot(
            _currentAdministrator.IsAuthenticated,
            _currentAdministrator.AdministratorId,
            _auditContextAccessor.Current);
    }
}

internal sealed class PlayerAssetsApiProbe
{
    public long MutationRevision { get; set; } = 8;

    public long AttributesRevision { get; set; } = 7;

    public PlayerAssetsException? ExceptionToThrow { get; set; }

    public List<EquipApiCall> EquipCalls { get; } = [];

    public List<UnequipApiCall> UnequipCalls { get; } = [];

    public List<GetAttributesApiCall> GetAttributesCalls { get; } = [];

    public int CallCount => EquipCalls.Count + UnequipCalls.Count + GetAttributesCalls.Count;

    public void ThrowIfConfigured()
    {
        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }
    }
}

internal sealed record EquipApiCall(
    bool IsAuthenticated,
    Guid? PlayerId,
    Guid CharacterId,
    string Slot,
    Guid OwnedItemId,
    PlayerAssetsMutationContext MutationContext);

internal sealed record UnequipApiCall(
    bool IsAuthenticated,
    Guid? PlayerId,
    Guid CharacterId,
    string Slot,
    PlayerAssetsMutationContext MutationContext);

internal sealed record GetAttributesApiCall(
    bool IsAuthenticated,
    Guid? PlayerId,
    Guid CharacterId);

internal sealed class CapturingPlayerAssetsAppService : IPlayerAssetsAppService
{
    private readonly ICurrentPlayerContext _currentPlayer;
    private readonly PlayerAssetsApiProbe _probe;

    public CapturingPlayerAssetsAppService(
        ICurrentPlayerContext currentPlayer,
        PlayerAssetsApiProbe probe)
    {
        _currentPlayer = currentPlayer;
        _probe = probe;
    }

    public Task<PlayerAssetsMutationResultDto> EquipAsync(
        Guid characterId,
        string slot,
        EquipEquipmentRequest request,
        PlayerAssetsMutationContext mutationContext,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _probe.EquipCalls.Add(new EquipApiCall(
            _currentPlayer.IsAuthenticated,
            _currentPlayer.PlayerId,
            characterId,
            slot,
            request.OwnedItemId,
            mutationContext));
        _probe.ThrowIfConfigured();
        return Task.FromResult(new PlayerAssetsMutationResultDto(_probe.MutationRevision));
    }

    public Task<PlayerAssetsMutationResultDto> UnequipAsync(
        Guid characterId,
        string slot,
        PlayerAssetsMutationContext mutationContext,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _probe.UnequipCalls.Add(new UnequipApiCall(
            _currentPlayer.IsAuthenticated,
            _currentPlayer.PlayerId,
            characterId,
            slot,
            mutationContext));
        _probe.ThrowIfConfigured();
        return Task.FromResult(new PlayerAssetsMutationResultDto(_probe.MutationRevision));
    }

    public Task<CharacterAttributesDto> GetAttributesAsync(
        Guid characterId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _probe.GetAttributesCalls.Add(new GetAttributesApiCall(
            _currentPlayer.IsAuthenticated,
            _currentPlayer.PlayerId,
            characterId));
        _probe.ThrowIfConfigured();
        return Task.FromResult(new CharacterAttributesDto(
            characterId,
            _probe.AttributesRevision,
            [new CharacterAttributeDto("attack", 10, 7, 17)],
            []));
    }
}
