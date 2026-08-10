using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NovaCore.BuildingBlocks.Modularity;
using NovaGame.Application;
using NovaGame.Application.Auditing;
using NovaGame.Host.EntityFrameworkCore;
using SwordMaster.Application;
using SwordMaster.DbMigrator.Auditing;
using SwordMaster.DbMigrator.PlayerAssets;
using SwordMaster.Host.EntityFrameworkCore;

namespace SwordMaster.DbMigrator;

[DependsOn(
    typeof(NovaGameApplicationModule),
    typeof(NovaGameEntityFrameworkCoreModule),
    typeof(SwordMasterApplicationModule),
    typeof(SwordMasterEntityFrameworkCoreModule))]
public sealed class SwordMasterDbMigratorModule : NovaCoreModule
{
    public override void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.TryAddScoped<
            IRequestAuditContextAccessor,
            SwordMasterDbMigratorRequestAuditContextAccessor>();
        services
            .AddOptions<SwordMasterPlayerAssetsDevelopmentSeedOptions>()
            .Bind(configuration.GetSection(
                SwordMasterPlayerAssetsDevelopmentSeedOptions.SectionName))
            .Validate(
                SwordMasterPlayerAssetsDevelopmentSeedOptions.IsValid,
                $"{SwordMasterPlayerAssetsDevelopmentSeedOptions.SectionName} must define valid non-empty identifiers, a configured item identifier, and base attributes.")
            .ValidateOnStart();
        services.AddScoped<SwordMasterPlayerAssetsDevelopmentSeedRunner>();
    }
}
