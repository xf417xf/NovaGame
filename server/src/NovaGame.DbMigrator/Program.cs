using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NovaCore.BuildingBlocks.Data;
using NovaCore.BuildingBlocks.Modularity;
using NovaGame.DbMigrator;
using NovaGame.Deployment;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

try
{
    NovaGameProductionConfigurationValidator.Validate(
        builder.Configuration,
        builder.Environment.EnvironmentName);

    if (args.Contains("--validate-configuration", StringComparer.Ordinal))
    {
        Console.WriteLine("Production configuration validation passed.");
        return 0;
    }

    builder.Services.AddNovaCoreModules<NovaGameDbMigratorModule>(builder.Configuration);

    using var host = builder.Build();
    await using var scope = host.Services.CreateAsyncScope();
    var logger = scope.ServiceProvider
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("NovaGame.DbMigrator");
    var dbContexts = scope.ServiceProvider
        .GetServices<DbContext>()
        .GroupBy(dbContext => dbContext.GetType())
        .Select(group => group.First())
        .OrderBy(dbContext => dbContext.GetType().FullName, StringComparer.Ordinal)
        .ToArray();

    foreach (var dbContext in dbContexts)
    {
        logger.LogInformation("Applying migrations for {DbContextType}.", dbContext.GetType().FullName);
        await dbContext.Database.MigrateAsync();
    }

    logger.LogInformation("Running data seed contributors.");
    await scope.ServiceProvider.GetRequiredService<IDataSeedManager>().SeedAsync();
    logger.LogInformation("Database migration and seeding completed.");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}
