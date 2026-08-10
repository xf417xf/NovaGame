using NovaCore.AspNetCore.Modularity;
using NovaCore.BuildingBlocks.Modularity;
using NovaGame.Deployment;
using SwordMaster.Host.EntityFrameworkCore.DependencyInjection;

namespace SwordMaster.Api;

public partial class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        NovaGameProductionConfigurationValidator.Validate(
            builder.Configuration,
            builder.Environment.EnvironmentName);
        SwordMasterDatabaseConfigurationValidator.Validate(builder.Configuration);

        builder.Services.AddNovaCoreModules<SwordMasterApiModule>(builder.Configuration);

        var app = builder.Build();

        app.UseNovaCoreModules();

        app.Run();
    }
}
