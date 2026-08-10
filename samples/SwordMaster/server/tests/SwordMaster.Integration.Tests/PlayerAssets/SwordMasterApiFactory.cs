using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

    public PlayerAssetsApiProbe Probe { get; } = new();

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
            services.RemoveAll<IPlayerAssetsAppService>();
            services.AddSingleton(Probe);
            services.AddScoped<IPlayerAssetsAppService, CapturingPlayerAssetsAppService>();
        });
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
