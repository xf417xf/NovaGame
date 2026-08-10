using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NovaCore.BuildingBlocks.Data;
using NovaCore.BuildingBlocks.Modularity;
using NovaGame.Application.Operations.AdminAccess;
using NovaGame.Host.EntityFrameworkCore;
using SwordMaster.DbMigrator;
using SwordMaster.DbMigrator.PlayerAssets;
using SwordMaster.Host.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

try
{
    SwordMasterDbMigratorConfigurationValidator.Validate(builder.Configuration);
    builder.Services.AddNovaCoreModules<SwordMasterDbMigratorModule>(builder.Configuration);

    using var host = builder.Build();
    await host.StartAsync();
    try
    {
        if (args.Contains("--validate-configuration", StringComparer.Ordinal))
        {
            Console.WriteLine("SwordMaster database configuration validation passed.");
            return 0;
        }

        var logger = host.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("SwordMaster.DbMigrator");

        await ApplyMigrationsAsync(host.Services, logger);

        logger.LogInformation("Running configured NovaGame administrator seed.");
        await SeedNovaGameAdministratorAsync(host.Services);

        if (ShouldRunSwordMasterDevelopmentSeed(builder.Environment))
        {
            logger.LogInformation(
                "Running SwordMaster validation-profile seed in {EnvironmentName}.",
                builder.Environment.EnvironmentName);
            await SeedSwordMasterPlayerAssetsAsync(host.Services);
        }
        else
        {
            logger.LogInformation(
                "Skipping SwordMaster validation-profile seed in {EnvironmentName}.",
                builder.Environment.EnvironmentName);
        }

        logger.LogInformation("SwordMaster database migration completed.");
        return 0;
    }
    finally
    {
        await host.StopAsync();
    }
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

static bool ShouldRunSwordMasterDevelopmentSeed(IHostEnvironment environment)
{
    return environment.IsDevelopment() || environment.IsEnvironment("Testing");
}

static async Task ApplyMigrationsAsync(
    IServiceProvider services,
    ILogger logger,
    CancellationToken cancellationToken = default)
{
    await using var scope = services.CreateAsyncScope();

    logger.LogInformation("Applying NovaGame database migrations.");
    await scope.ServiceProvider
        .GetRequiredService<NovaGameDbContext>()
        .Database
        .MigrateAsync(cancellationToken);

    logger.LogInformation("Applying SwordMaster database migrations.");
    await scope.ServiceProvider
        .GetRequiredService<SwordMasterDbContext>()
        .Database
        .MigrateAsync(cancellationToken);
}

static async Task SeedNovaGameAdministratorAsync(
    IServiceProvider services,
    CancellationToken cancellationToken = default)
{
    await using var scope = services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<NovaGameDbContext>();
    var contributor = scope.ServiceProvider
        .GetServices<IDataSeedContributor>()
        .OfType<AdministratorSeedContributor>()
        .Single();

    await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
    await contributor.SeedAsync(new DataSeedContext(), cancellationToken);
    await dbContext.SaveChangesAsync(cancellationToken);
    await transaction.CommitAsync(cancellationToken);
}

static async Task SeedSwordMasterPlayerAssetsAsync(
    IServiceProvider services,
    CancellationToken cancellationToken = default)
{
    await using var scope = services.CreateAsyncScope();
    await scope.ServiceProvider
        .GetRequiredService<SwordMasterPlayerAssetsDevelopmentSeedRunner>()
        .SeedAsync(cancellationToken);
}
