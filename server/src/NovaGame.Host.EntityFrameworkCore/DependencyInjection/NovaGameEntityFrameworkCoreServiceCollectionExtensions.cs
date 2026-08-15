using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using NovaCore.Framework.EntityFrameworkCore.DependencyInjection;
using NovaGame.Domain.Content.GameConfig;
using NovaGame.Domain.Operations.AdminAccess;
using NovaGame.Host.EntityFrameworkCore.Content.GameConfig;
using NovaGame.Host.EntityFrameworkCore.Operations.AdminAccess;

namespace NovaGame.Host.EntityFrameworkCore.DependencyInjection;

public static class NovaGameEntityFrameworkCoreServiceCollectionExtensions
{
    private const string EntityFrameworkCoreSectionName = "NovaGame:EntityFrameworkCore";
    private const string DefaultConnectionStringName = "Default";
    private const string DefaultServerVersion = "8.4.0";

    public static IServiceCollection AddNovaGameEntityFrameworkCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.ConfigureNovaCoreDbContexts((_, options) =>
        {
            var section = configuration.GetSection(EntityFrameworkCoreSectionName);
            var connectionStringName = section["ConnectionStringName"] ?? DefaultConnectionStringName;
            var connectionString = configuration.GetConnectionString(connectionStringName);

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"Connection string '{connectionStringName}' is required for the MySQL database.");
            }

            ConfigureMySql(
                options,
                connectionString,
                section["ServerVersion"] ?? DefaultServerVersion);
        });

        services.AddNovaGameEntityFrameworkCore();
        return services;
    }

    public static IServiceCollection AddNovaGameEntityFrameworkCore(
        this IServiceCollection services,
        Action<IServiceProvider, DbContextOptionsBuilder>? configureOptions = null)
    {
        services.AddNovaCoreDbContext<NovaGameDbContext>(configureOptions);
        services.AddScoped<IAdministratorRepository, AdministratorRepository>();
        services.AddScoped<IAdminRoleRepository, AdminRoleRepository>();
        services.AddScoped<IRefreshTokenSessionRepository, RefreshTokenSessionRepository>();
        services.AddScoped<IAdminAccessAuditRepository, AdminAccessAuditRepository>();
        services.AddScoped<AdminAccessManagementRepository>();
        services.AddScoped<IAdminAccessManagementRepository>(serviceProvider =>
            serviceProvider.GetRequiredService<AdminAccessManagementRepository>());
        services.AddScoped<IAdminAccessManagementReadSnapshot>(serviceProvider =>
            serviceProvider.GetRequiredService<AdminAccessManagementRepository>());
        services.AddScoped<IAdminAccessCommandReceiptRepository, AdminAccessCommandReceiptRepository>();
        services.AddScoped<IGameConfigDraftRepository, GameConfigDraftRepository>();
        services.AddScoped<IPublishedGameConfigVersionRepository, PublishedGameConfigVersionRepository>();
        services.AddScoped<IGameConfigEnvironmentRepository, GameConfigEnvironmentRepository>();
        services.AddScoped<IGameConfigAuditRepository, GameConfigAuditRepository>();
        return services;
    }

    internal static void ConfigureMySql(
        DbContextOptionsBuilder options,
        string connectionString,
        string serverVersion)
    {
        if (!Version.TryParse(serverVersion, out var parsedServerVersion) ||
            parsedServerVersion.Major != 8 ||
            parsedServerVersion.Minor != 4)
        {
            throw new InvalidOperationException(
                $"'{EntityFrameworkCoreSectionName}:ServerVersion' must target MySQL 8.4.x.");
        }

        // NovaCore begins explicit transactions outside an EF replay delegate. Provider retries
        // would reject that boundary and cannot safely replay the complete application operation.
        var connectionStringBuilder = new MySqlConnectionStringBuilder(connectionString)
        {
            DateTimeKind = MySqlDateTimeKind.Utc
        };

        options.UseMySql(
            connectionStringBuilder.ConnectionString,
            new MySqlServerVersion(parsedServerVersion),
            mySqlOptions => mySqlOptions.MigrationsAssembly(
                typeof(NovaGameDbContext).Assembly.FullName));
    }
}
