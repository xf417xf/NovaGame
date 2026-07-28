using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NovaCore.BuildingBlocks.Modularity;
using NovaCore.Framework.EntityFrameworkCore;
using NovaGame.Domain;
using NovaGame.Host.EntityFrameworkCore.DependencyInjection;

namespace NovaGame.Host.EntityFrameworkCore;

[DependsOn(typeof(NovaGameDomainModule), typeof(NovaCoreFrameworkEfCoreModule))]
public sealed class NovaGameEntityFrameworkCoreModule : NovaCoreModule
{
    public override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddNovaGameEntityFrameworkCore(configuration);
    }
}
