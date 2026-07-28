using System.Globalization;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NovaCore.Shared.Json;
using NovaCore.Shared.Results;
using NovaCore.Shared.Security;
using NovaGame.Application.Contracts.Operations.AdminAccess;
using NovaGame.Application.Operations.AdminAccess;
using NovaGame.Domain.Shared.Operations.AdminAccess;

namespace NovaGame.Api.Operations.AdminAccess;

public static class AdminAccessApiServiceCollectionExtensions
{
    public const string LoginRateLimitPolicyName = "admin-access-login";

    public static IServiceCollection AddAdminAccessApi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<AdminAccessJwtOptions>()
            .Bind(configuration.GetSection(AdminAccessJwtOptions.SectionName))
            .Validate(
                AdminAccessJwtOptions.IsValid,
                $"{AdminAccessJwtOptions.SectionName} requires an issuer, audience, a base64url signing key containing at least 32 bytes, and a clock skew between zero and five minutes.")
            .ValidateOnStart();

        services
            .AddOptions<AdminAccessLoginRateLimitOptions>()
            .Bind(configuration.GetSection(AdminAccessLoginRateLimitOptions.SectionName))
            .Validate(
                AdminAccessLoginRateLimitOptions.IsValid,
                $"{AdminAccessLoginRateLimitOptions.SectionName} contains invalid limits.")
            .ValidateOnStart();

        services
            .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<AdminAccessJwtOptions>>((options, configuredOptions) =>
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
                    NameClaimType = SecurityClaimNames.UserName,
                    RoleClaimType = SecurityClaimNames.Role
                };
                options.Events = new JwtBearerEvents
                {
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        context.Response.ContentType = "application/json";
                        context.Response.Headers.WWWAuthenticate = "Bearer";
                        await JsonSerializer.SerializeAsync(
                            context.Response.Body,
                            ApiResponse.Failure(
                                AdminAccessErrorCodes.Unauthorized,
                                "Authentication is required."),
                            NovaCoreJsonOptions.CreateDefault(),
                            context.HttpContext.RequestAborted);
                    },
                    OnForbidden = async context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        context.Response.ContentType = "application/json";
                        await JsonSerializer.SerializeAsync(
                            context.Response.Body,
                            ApiResponse.Failure(
                                AdminAccessErrorCodes.Forbidden,
                                "The current administrator does not have permission to perform this action."),
                            NovaCoreJsonOptions.CreateDefault(),
                            context.HttpContext.RequestAborted);
                    }
                };
            });

        var rateLimit = configuration
            .GetSection(AdminAccessLoginRateLimitOptions.SectionName)
            .Get<AdminAccessLoginRateLimitOptions>() ?? new AdminAccessLoginRateLimitOptions();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(LoginRateLimitPolicyName, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rateLimit.PermitLimit,
                        Window = rateLimit.Window,
                        QueueLimit = rateLimit.QueueLimit,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        AutoReplenishment = true
                    }));
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds)
                        .ToString(CultureInfo.InvariantCulture);
                }

                var loginPolicy = context.HttpContext
                    .GetEndpoint()?
                    .Metadata
                    .GetOrderedMetadata<EnableRateLimitingAttribute>()
                    .Any(attribute => attribute.PolicyName == LoginRateLimitPolicyName) == true;

                if (loginPolicy)
                {
                    context.HttpContext.Response.ContentType = "application/json";
                    await JsonSerializer.SerializeAsync(
                        context.HttpContext.Response.Body,
                        ApiResponse.Failure(
                            AdminAccessErrorCodes.LoginRateLimited,
                            "Too many login attempts. Try again later."),
                        NovaCoreJsonOptions.CreateDefault(),
                        cancellationToken);
                    return;
                }

                context.HttpContext.Response.ContentType = "application/problem+json";
                await JsonSerializer.SerializeAsync(
                    context.HttpContext.Response.Body,
                    new ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Too Many Requests",
                        Detail = "The request rate limit was exceeded."
                    },
                    cancellationToken: cancellationToken);
            };
        });

        services.AddScoped<IAdminAccessAppService, AdminAccessAppService>();
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        return services;
    }
}
