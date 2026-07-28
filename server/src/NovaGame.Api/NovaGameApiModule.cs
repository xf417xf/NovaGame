using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.OpenApi.Models;
using NovaCore.AspNetCore.Cors;
using NovaCore.AspNetCore.ForwardedHeaders;
using NovaCore.AspNetCore.Localization;
using NovaCore.AspNetCore.Modularity;
using NovaCore.AspNetCore.ResponseCompression;
using NovaCore.Authentication.JwtBearer;
using NovaCore.BuildingBlocks.Auditing;
using NovaCore.BuildingBlocks.Authorization;
using NovaCore.BuildingBlocks.Correlation;
using NovaCore.BuildingBlocks.ExceptionHandling;
using NovaCore.BuildingBlocks.Features;
using NovaCore.BuildingBlocks.HealthChecks;
using NovaCore.BuildingBlocks.Modularity;
using NovaCore.BuildingBlocks.MultiTenancy;
using NovaCore.HttpClient.Resilience;
using NovaCore.ObjectMapping.Mapster;
using NovaCore.Observability.OpenTelemetry;
using NovaCore.Observability.OpenTelemetry.DependencyInjection;
using NovaCore.Shared.Json;
using NovaCore.Shared.Results;
using NovaCore.Validation.FluentValidation;
using NovaGame.Api.Auditing;
using NovaGame.Api.Content.GameConfig;
using NovaGame.Api.Operations.AdminAccess;
using NovaGame.Application;
using NovaGame.Application.Auditing;
using NovaGame.Host.EntityFrameworkCore;

namespace NovaGame.Api;

[DependsOn(
    typeof(NovaCoreAspNetCoreModule),
    typeof(NovaCoreAuthenticationJwtBearerModule),
    typeof(NovaCoreHttpClientResilienceModule),
    typeof(NovaCoreObjectMappingMapsterModule),
    typeof(NovaCoreObservabilityOpenTelemetryModule),
    typeof(NovaCoreValidationFluentValidationModule),
    typeof(NovaGameApplicationModule),
    typeof(NovaGameEntityFrameworkCoreModule))]
public sealed class NovaGameApiModule : NovaCoreModule, IAspNetCoreModule
{
    public override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddControllers()
            .AddMvcOptions(options => options.AddNovaCoreFeatureFilter())
            .AddJsonOptions(options => NovaCoreJsonOptions.Configure(options.JsonSerializerOptions))
            .ConfigureApiBehaviorOptions(options =>
            {
                options.InvalidModelStateResponseFactory = context =>
                {
                    var errors = context.ModelState
                        .Where(item => item.Value?.Errors.Count > 0)
                        .ToDictionary(
                            item => item.Key,
                            item => item.Value!.Errors.Select(error => error.ErrorMessage).ToArray());

                    return new BadRequestObjectResult(ApiResponse.Failure(
                        NovaCoreErrorCodes.ValidationError,
                        "The request payload is invalid.",
                        errors));
                };
            });

        services.AddEndpointsApiExplorer();
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IAuthorizationHandler, PermissionAuthorizationHandler>());
        services.AddScoped<IRequestAuditContextAccessor, HttpRequestAuditContextAccessor>();
        services.AddAdminAccessApi(configuration);
        services.AddGameConfigApi();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "NovaGame API",
                Version = "v1",
                Description = "NovaGame modular game-server framework API"
            });
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT"
            });
            options.OperationFilter<AdminAccessOpenApiOperationFilter>();
        });
    }

    public void ConfigureApplication(WebApplication app)
    {
        app.UseNovaCoreForwardedHeaders();
        app.UseNovaCoreRequestLocalization();
        app.UseNovaCoreResponseCompression();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/swagger/v1/swagger.json", "NovaGame API v1");
                options.DocumentTitle = "NovaGame API";
            });
        }

        app.UseNovaCoreCorrelationId();
        app.UseNovaCoreExceptionHandling();
        app.UseHttpsRedirection();
        app.UseRouting();
        app.UseNovaCoreCors();
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseNovaCoreMultiTenancy();
        app.UseNovaCoreAuditing();
        app.UseAuthorization();
        app.UseNovaCoreHealthChecks();
        app.MapNovaCorePrometheusScrapingEndpoint();
        app.MapControllers();
    }

    public void OnApplicationInitialization(WebApplication app)
    {
    }

    public void OnApplicationShutdown(WebApplication app)
    {
    }
}
