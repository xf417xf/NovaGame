using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NovaCore.BuildingBlocks.Modularity;
using NovaGame.Application;
using NovaGame.Application.Auditing;
using NovaGame.DbMigrator.Auditing;
using NovaGame.Host.EntityFrameworkCore;

namespace NovaGame.DbMigrator;

[DependsOn(typeof(NovaGameApplicationModule), typeof(NovaGameEntityFrameworkCoreModule))]
public sealed class NovaGameDbMigratorModule : NovaCoreModule
{
    public override void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.TryAddScoped<
            IRequestAuditContextAccessor,
            DbMigratorRequestAuditContextAccessor>();
    }
}
