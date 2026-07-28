using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NovaCore.BuildingBlocks.Modularity;
using NovaCore.BuildingBlocks.Permissions;
using NovaGame.Domain.Shared.Content.GameConfig;
using NovaGame.Domain.Shared.Operations.AdminAccess;

namespace NovaGame.Domain.Shared;

public sealed class NovaGameDomainSharedModule : NovaCoreModule
{
    public override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddTransient<IPermissionDefinitionProvider, AdminAccessPermissionDefinitionProvider>();
        services.AddTransient<IPermissionDefinitionProvider, GameConfigPermissionDefinitionProvider>();
    }
}
