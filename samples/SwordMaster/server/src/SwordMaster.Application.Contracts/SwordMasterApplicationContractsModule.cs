using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NovaCore.BuildingBlocks.Modularity;
using NovaCore.BuildingBlocks.Permissions;
using SwordMaster.Application.Contracts.PlayerAssets;

namespace SwordMaster.Application.Contracts;

public sealed class SwordMasterApplicationContractsModule : NovaCoreModule
{
    public override void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddTransient<
            IPermissionDefinitionProvider,
            PlayerEquipmentGrantPermissionDefinitionProvider>();
    }
}
