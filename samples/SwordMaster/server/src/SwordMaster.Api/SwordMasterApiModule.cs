using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NovaCore.BuildingBlocks.Modularity;
using NovaGame.Api;
using SwordMaster.Application;
using SwordMaster.Api.PlayerAssets;
using SwordMaster.Host.EntityFrameworkCore;

namespace SwordMaster.Api;

[DependsOn(
    typeof(NovaGameApiModule),
    typeof(SwordMasterApplicationModule),
    typeof(SwordMasterEntityFrameworkCoreModule))]
public sealed class SwordMasterApiModule : NovaCoreModule
{
    public override void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSwordMasterPlayerAssetsApi(configuration);
    }
}
