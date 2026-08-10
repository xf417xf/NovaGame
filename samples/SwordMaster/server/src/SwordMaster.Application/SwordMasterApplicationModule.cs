using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NovaCore.BuildingBlocks.Modularity;
using NovaGame.Domain;
using SwordMaster.Application.Contracts;
using SwordMaster.Application.Contracts.PlayerAssets;
using SwordMaster.Application.PlayerAssets;
using SwordMaster.Domain;

namespace SwordMaster.Application;

[DependsOn(
    typeof(SwordMasterApplicationContractsModule),
    typeof(SwordMasterDomainModule),
    typeof(NovaGameDomainModule))]
public sealed class SwordMasterApplicationModule : NovaCoreModule
{
    public override void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services
            .AddOptions<PlayerAssetsGameConfigOptions>()
            .Bind(configuration.GetSection(PlayerAssetsGameConfigOptions.SectionName))
            .Validate(
                PlayerAssetsGameConfigOptions.IsValid,
                $"{PlayerAssetsGameConfigOptions.SectionName}:Environment must be a lowercase safe slug.")
            .ValidateOnStart();
        services.TryAddSingleton<PlayerAssetsGameConfigSnapshotParser>();
        services.AddScoped<IEquipmentDefinitionProvider, GameConfigEquipmentDefinitionProvider>();
        services.AddScoped<IPlayerAssetsAppService, PlayerAssetsAppService>();
    }
}
