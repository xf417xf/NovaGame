using NovaCore.AspNetCore.Modularity;
using NovaCore.BuildingBlocks.Modularity;
using NovaGame.Api;
using NovaGame.Deployment;

var builder = WebApplication.CreateBuilder(args);

NovaGameProductionConfigurationValidator.Validate(
    builder.Configuration,
    builder.Environment.EnvironmentName);

builder.Services.AddNovaCoreModules<NovaGameApiModule>(builder.Configuration);

var app = builder.Build();

app.UseNovaCoreModules();

app.Run();

public partial class Program;
