using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NovaCore.BuildingBlocks.Modularity;
using NovaCore.Framework.EntityFrameworkCore;
using SwordMaster.Domain;
using SwordMaster.Host.EntityFrameworkCore.DependencyInjection;

namespace SwordMaster.Host.EntityFrameworkCore;

[DependsOn(typeof(SwordMasterDomainModule), typeof(NovaCoreFrameworkEfCoreModule))]
public sealed class SwordMasterEntityFrameworkCoreModule : NovaCoreModule
{
    public override void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSwordMasterEntityFrameworkCore(configuration);
    }
}
