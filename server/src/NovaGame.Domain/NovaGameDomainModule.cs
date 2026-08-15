using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NovaCore.BuildingBlocks.Modularity;
using NovaGame.Domain.Content.GameConfig;
using NovaGame.Domain.Operations.AdminAccess;
using NovaGame.Domain.Shared;

namespace NovaGame.Domain;

[DependsOn(typeof(NovaGameDomainSharedModule))]
public sealed class NovaGameDomainModule : NovaCoreModule
{
    public override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<AdminAccessManagementPolicy>();
        services.AddSingleton<GameConfigContentProcessor>();
        services.AddSingleton<GameConfigSnapshotValidator>();
        services.AddSingleton<GameConfigSnapshotBuilder>();
        services.AddSingleton<RefreshTokenFamilyPolicy>();
    }
}
