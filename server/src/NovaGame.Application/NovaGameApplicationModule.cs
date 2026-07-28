using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NovaCore.BuildingBlocks.Data;
using NovaCore.BuildingBlocks.EventBus;
using NovaCore.BuildingBlocks.Modularity;
using NovaCore.BuildingBlocks.Permissions;
using NovaGame.Application.Contracts;
using NovaGame.Application.Contracts.Content.GameConfig;
using NovaGame.Application.Content.GameConfig;
using NovaGame.Application.Operations.AdminAccess;
using NovaGame.Domain;

namespace NovaGame.Application;

[DependsOn(typeof(NovaGameApplicationContractsModule), typeof(NovaGameDomainModule))]
public sealed class NovaGameApplicationModule : NovaCoreModule
{
    public override void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddNovaCoreEventBus();
        services.Configure<AdminAccessSeedOptions>(
            configuration.GetSection(AdminAccessSeedOptions.SectionName));
        services.AddSingleton<IPasswordHashingService, AspNetCorePasswordHashingService>();
        services.AddSingleton<IRefreshTokenService, RefreshTokenService>();
        services.AddScoped<IGameConfigAppService, GameConfigAppService>();
        services.AddTransient<IDataSeedContributor, AdministratorSeedContributor>();
        services.AddScoped<DatabasePermissionChecker>();
        services.Replace(ServiceDescriptor.Scoped<IPermissionChecker>(serviceProvider =>
            serviceProvider.GetRequiredService<DatabasePermissionChecker>()));
    }
}
