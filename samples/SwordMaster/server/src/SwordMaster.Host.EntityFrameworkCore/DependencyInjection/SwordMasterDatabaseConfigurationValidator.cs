using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace SwordMaster.Host.EntityFrameworkCore.DependencyInjection;

public static class SwordMasterDatabaseConfigurationValidator
{
    private const string NovaGameConnectionStringName = "NovaGame";
    private const string SwordMasterConnectionStringName = "SwordMaster";

    public static void Validate(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var errors = new List<string>();
        ValidateConfiguredName(
            configuration,
            "NovaGame:EntityFrameworkCore:ConnectionStringName",
            NovaGameConnectionStringName,
            errors);
        ValidateConfiguredName(
            configuration,
            "SwordMaster:EntityFrameworkCore:ConnectionStringName",
            SwordMasterConnectionStringName,
            errors);
        ValidateServerVersion(
            configuration,
            "NovaGame:EntityFrameworkCore:ServerVersion",
            errors);
        ValidateServerVersion(
            configuration,
            "SwordMaster:EntityFrameworkCore:ServerVersion",
            errors);

        var novaGameDatabase = ReadDatabaseName(
            configuration,
            NovaGameConnectionStringName,
            errors);
        var swordMasterDatabase = ReadDatabaseName(
            configuration,
            SwordMasterConnectionStringName,
            errors);
        if (novaGameDatabase is not null &&
            swordMasterDatabase is not null &&
            string.Equals(
                novaGameDatabase,
                swordMasterDatabase,
                StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(
                "ConnectionStrings:NovaGame and ConnectionStrings:SwordMaster must target distinct MySQL databases.");
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "SwordMaster database configuration validation failed:" + Environment.NewLine +
                string.Join(Environment.NewLine, errors.Select(error => $"- {error}")));
        }
    }

    private static void ValidateConfiguredName(
        IConfiguration configuration,
        string configurationKey,
        string expectedName,
        ICollection<string> errors)
    {
        if (!string.Equals(
                configuration[configurationKey],
                expectedName,
                StringComparison.Ordinal))
        {
            errors.Add($"{configurationKey} must be '{expectedName}'.");
        }
    }

    private static string? ReadDatabaseName(
        IConfiguration configuration,
        string connectionStringName,
        ICollection<string> errors)
    {
        var connectionString = configuration.GetConnectionString(connectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            errors.Add($"ConnectionStrings:{connectionStringName} is required.");
            return null;
        }

        try
        {
            var builder = new MySqlConnectionStringBuilder(connectionString);
            if (string.IsNullOrWhiteSpace(builder.Database))
            {
                errors.Add(
                    $"ConnectionStrings:{connectionStringName} must specify a database name.");
                return null;
            }

            return builder.Database.Trim();
        }
        catch (ArgumentException)
        {
            errors.Add(
                $"ConnectionStrings:{connectionStringName} is not a valid MySQL connection string.");
            return null;
        }
    }

    private static void ValidateServerVersion(
        IConfiguration configuration,
        string configurationKey,
        ICollection<string> errors)
    {
        if (!Version.TryParse(configuration[configurationKey], out var serverVersion) ||
            serverVersion.Major != 8 ||
            serverVersion.Minor != 4)
        {
            errors.Add($"'{configurationKey}' must target MySQL 8.4.x.");
        }
    }
}
