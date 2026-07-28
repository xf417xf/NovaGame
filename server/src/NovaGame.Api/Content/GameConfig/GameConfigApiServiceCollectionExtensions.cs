using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NovaGame.Api.Content.GameConfig;

public static class GameConfigApiServiceCollectionExtensions
{
    public static IServiceCollection AddGameConfigApi(this IServiceCollection services)
    {
        services.TryAddScoped<GameConfigExceptionFilter>();
        services.Configure<MvcOptions>(options =>
            options.Filters.AddService<GameConfigExceptionFilter>());
        return services;
    }
}
