using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NovaCore.Shared.Json;
using NovaCore.Shared.Results;
using NovaCore.Shared.Security;
using SwordMaster.Application.Contracts.PlayerAssets;
using SwordMaster.Application.PlayerAssets;

namespace SwordMaster.Api.PlayerAssets;

public static class SwordMasterPlayerAuthentication
{
    public const string Scheme = "SwordMasterPlayer";
    public const string TokenUseClaim = "token_use";
    public const string PlayerTokenUse = "player";

    public static IServiceCollection AddSwordMasterPlayerAssetsApi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<SwordMasterPlayerJwtOptions>()
            .Bind(configuration.GetSection(SwordMasterPlayerJwtOptions.SectionName))
            .Validate(
                SwordMasterPlayerJwtOptions.IsValid,
                $"{SwordMasterPlayerJwtOptions.SectionName} requires an issuer, audience, a base64url signing key containing at least 32 bytes, and a clock skew between zero and five minutes.")
            .ValidateOnStart();

        // Keep NovaGame's administrator Bearer handler as the default scheme.
        services
            .AddAuthentication()
            .AddJwtBearer(Scheme, _ => { });
        services
            .AddOptions<JwtBearerOptions>(Scheme)
            .Configure<IOptions<SwordMasterPlayerJwtOptions>>((options, configuredOptions) =>
            {
                var configured = configuredOptions.Value;
                options.Authority = null;
                options.MetadataAddress = string.Empty;
                options.Audience = null;
                options.RequireHttpsMetadata = false;
                options.MapInboundClaims = false;
                options.SaveToken = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = configured.Issuer,
                    ValidateAudience = true,
                    ValidAudience = configured.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        configured.GetSigningKeyBytes()),
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ClockSkew = configured.ClockSkew,
                    AuthenticationType = Scheme,
                    NameClaimType = SecurityClaimNames.UserName,
                    RoleClaimType = SecurityClaimNames.Role
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = ValidatePlayerClaimsAsync,
                    OnChallenge = WriteUnauthorizedResponseAsync
                };
            });

        services.TryAddScoped<ICurrentPlayerContext, HttpCurrentPlayerContext>();
        services.TryAddScoped<PlayerAssetsOperationLogger>();
        services.TryAddScoped<PlayerAssetsExceptionFilter>();
        services.TryAddScoped<
            ICurrentSwordMasterAdministrator,
            HttpCurrentSwordMasterAdministrator>();
        services.TryAddScoped<
            IPlayerEquipmentGrantAuditContextAccessor,
            HttpPlayerEquipmentGrantAuditContextAccessor>();
        services.TryAddScoped<PlayerEquipmentGrantOperationLogger>();
        services.TryAddScoped<PlayerEquipmentGrantExceptionFilter>();
        services.TryAddScoped<PlayerEquipmentGrantUnsupportedMediaTypeResultFilter>();
        services.Replace(
            ServiceDescriptor.Singleton<
                IAuthorizationMiddlewareResultHandler,
                PlayerEquipmentGrantAuthorizationMiddlewareResultHandler>());
        services.Configure<Microsoft.AspNetCore.Mvc.MvcOptions>(options =>
        {
            options.Filters.AddService<PlayerAssetsExceptionFilter>();
            options.Filters.AddService<PlayerEquipmentGrantExceptionFilter>();
            options.Filters.AddService<PlayerEquipmentGrantUnsupportedMediaTypeResultFilter>();
        });
        services.PostConfigure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(options =>
        {
            var fallbackFactory = options.InvalidModelStateResponseFactory;
            options.InvalidModelStateResponseFactory = context =>
            {
                if (!PlayerEquipmentGrantEndpoint.IsController(context.ActionDescriptor))
                {
                    return fallbackFactory(context);
                }

                context.HttpContext.RequestServices
                    .GetRequiredService<PlayerEquipmentGrantOperationLogger>()
                    .LogInvalidRequest(context.HttpContext);
                return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(
                    ApiResponse.Failure(
                        PlayerEquipmentGrantErrorCodes.InvalidRequest,
                        "The request is invalid."));
            };
        });

        services
            .AddControllers()
            .AddApplicationPart(typeof(NovaGame.Api.NovaGameApiModule).Assembly)
            .AddApplicationPart(typeof(SwordMasterApiModule).Assembly);
        services.AddSwaggerGen(options =>
        {
            options.AddSecurityDefinition(Scheme, new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Name = "Authorization",
                In = Microsoft.OpenApi.Models.ParameterLocation.Header,
                Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "SwordMaster player JWT. Administrator tokens are not valid for player endpoints."
            });
            options.OperationFilter<PlayerAssetsOpenApiOperationFilter>();
            options.OperationFilter<PlayerEquipmentGrantOpenApiOperationFilter>();
        });

        return services;
    }

    private static Task ValidatePlayerClaimsAsync(TokenValidatedContext context)
    {
        var identities = context.Principal?
            .Identities
            .Where(identity =>
                identity.IsAuthenticated &&
                string.Equals(identity.AuthenticationType, Scheme, StringComparison.Ordinal))
            .ToArray() ?? [];
        if (identities.Length != 1)
        {
            context.Fail("The token is not a valid SwordMaster player credential.");
            return Task.CompletedTask;
        }

        var playerIds = identities[0]
            .FindAll(SecurityClaimNames.UserId)
            .Select(claim => claim.Value)
            .ToArray();
        var tokenUses = identities[0]
            .FindAll(TokenUseClaim)
            .Select(claim => claim.Value)
            .ToArray();

        if (playerIds.Length != 1 ||
            !Guid.TryParse(playerIds[0], out var playerId) ||
            playerId == Guid.Empty ||
            tokenUses.Length != 1 ||
            !string.Equals(tokenUses[0], PlayerTokenUse, StringComparison.Ordinal))
        {
            context.Fail("The token is not a valid SwordMaster player credential.");
        }

        return Task.CompletedTask;
    }

    private static async Task WriteUnauthorizedResponseAsync(JwtBearerChallengeContext context)
    {
        context.HandleResponse();
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";
        context.Response.Headers.WWWAuthenticate = "Bearer";
        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            ApiResponse.Failure(
                PlayerAssetsErrorCodes.UnauthorizedPlayer,
                "An authenticated SwordMaster player is required."),
            NovaCoreJsonOptions.CreateDefault(),
            context.HttpContext.RequestAborted);
    }
}
