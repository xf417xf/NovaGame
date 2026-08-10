using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using NovaCore.Framework.EntityFrameworkCore.DependencyInjection;
using SwordMaster.Application.PlayerAssets;
using SwordMaster.Domain.PlayerAssets;
using SwordMaster.Host.EntityFrameworkCore.PlayerAssets;

namespace SwordMaster.Host.EntityFrameworkCore.DependencyInjection;

public static class SwordMasterEntityFrameworkCoreServiceCollectionExtensions
{
    private const string EntityFrameworkCoreSectionName = "SwordMaster:EntityFrameworkCore";
    private const string DefaultConnectionStringName = "SwordMaster";
    private const string DefaultServerVersion = "8.4.0";

    public static IServiceCollection AddSwordMasterEntityFrameworkCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(EntityFrameworkCoreSectionName);
        var connectionStringName = section["ConnectionStringName"] ?? DefaultConnectionStringName;
        var connectionString = configuration.GetConnectionString(connectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{connectionStringName}' is required for the SwordMaster MySQL database.");
        }

        return services.AddSwordMasterEntityFrameworkCore((_, options) =>
            ConfigureMySql(
                options,
                connectionString,
                section["ServerVersion"] ?? DefaultServerVersion));
    }

    public static IServiceCollection AddSwordMasterEntityFrameworkCore(
        this IServiceCollection services,
        Action<IServiceProvider, DbContextOptionsBuilder>? configureOptions = null)
    {
        services.AddNovaCoreDbContext<SwordMasterDbContext>(configureOptions);
        services.AddScoped<IPlayerAssetsTransactionManager, PlayerAssetsTransactionManager>();
        services.AddScoped<IPlayerAssetsProfileRepository, PlayerAssetsProfileRepository>();
        services.AddScoped<IPlayerAssetCommandReceiptRepository, PlayerAssetCommandReceiptRepository>();
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

        var connectionStringBuilder = new MySqlConnectionStringBuilder(connectionString)
        {
            DateTimeKind = MySqlDateTimeKind.Utc
        };

        options.UseMySql(
            connectionStringBuilder.ConnectionString,
            new MySqlServerVersion(parsedServerVersion),
            mySqlOptions => mySqlOptions.MigrationsAssembly(
                typeof(SwordMasterDbContext).Assembly.FullName));
    }
}
