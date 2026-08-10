using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SwordMasterApiProgram = SwordMaster.Api.Program;

namespace SwordMaster.Integration.Tests.RuntimeComposition;

internal sealed class SwordMasterRuntimeApiFactory : WebApplicationFactory<SwordMasterApiProgram>
{
    internal const string NovaGameDatabaseName = "novagame_runtime_composition";
    internal const string SwordMasterDatabaseName = "swordmaster_runtime_composition";

    private const string AdminSigningKey =
        "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY";
    private const string PlayerSigningKey =
        "YWJjZGVmMDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODk";

    private readonly Dictionary<string, string?> _previousEnvironmentVariables =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Action<IServiceCollection>? _configureTestServices;
    private bool _environmentRestored;

    public SwordMasterRuntimeApiFactory(
        IReadOnlyDictionary<string, string?>? overrides = null,
        Action<IServiceCollection>? configureTestServices = null)
    {
        _configureTestServices = configureTestServices;
        var settings = CreateBaselineSettings();
        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
            {
                settings[key] = value;
            }
        }

        SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Development");
        foreach (var (key, value) in settings)
        {
            SetEnvironmentVariable(key.Replace(":", "__", StringComparison.Ordinal), value);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        if (_configureTestServices is not null)
        {
            builder.ConfigureServices(_configureTestServices);
        }
    }

    protected override void Dispose(bool disposing)
    {
        try
        {
            base.Dispose(disposing);
        }
        finally
        {
            if (disposing)
            {
                RestoreEnvironment();
            }
        }
    }

    private static Dictionary<string, string?> CreateBaselineSettings()
    {
        return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ConnectionStrings:NovaGame"] =
                $"Server=127.0.0.1;Port=1;Database={NovaGameDatabaseName};User ID=unused;Password=unused;Connection Timeout=1;",
            ["ConnectionStrings:SwordMaster"] =
                $"Server=127.0.0.1;Port=1;Database={SwordMasterDatabaseName};User ID=unused;Password=unused;Connection Timeout=1;",
            ["NovaGame:EntityFrameworkCore:ConnectionStringName"] = "NovaGame",
            ["NovaGame:EntityFrameworkCore:ServerVersion"] = "8.4.0",
            ["SwordMaster:EntityFrameworkCore:ConnectionStringName"] = "SwordMaster",
            ["SwordMaster:EntityFrameworkCore:ServerVersion"] = "8.4.0",
            ["NovaGame:AdminAccess:Jwt:Issuer"] = "NovaGame.SwordMaster.Runtime.Tests",
            ["NovaGame:AdminAccess:Jwt:Audience"] = "NovaGame.SwordMaster.Admin.Runtime.Tests",
            ["NovaGame:AdminAccess:Jwt:SigningKey"] = AdminSigningKey,
            ["NovaGame:AdminAccess:Jwt:ClockSkew"] = "00:00:00",
            ["SwordMaster:PlayerAuthentication:Jwt:Issuer"] = "SwordMaster.Runtime.Tests",
            ["SwordMaster:PlayerAuthentication:Jwt:Audience"] = "SwordMaster.Player.Runtime.Tests",
            ["SwordMaster:PlayerAuthentication:Jwt:SigningKey"] = PlayerSigningKey,
            ["SwordMaster:PlayerAuthentication:Jwt:ClockSkew"] = "00:00:00",
            ["SwordMaster:GameConfig:Environment"] = "development",
            ["SwordMaster:PlayerAssets:DevelopmentSeed:PlayerId"] =
                "11111111-1111-1111-1111-111111111111",
            ["SwordMaster:PlayerAssets:DevelopmentSeed:CharacterId"] =
                "22222222-2222-2222-2222-222222222222",
            ["SwordMaster:PlayerAssets:DevelopmentSeed:OwnedItemId"] =
                "33333333-3333-3333-3333-333333333333",
            ["SwordMaster:PlayerAssets:DevelopmentSeed:ConfiguredItemId"] =
                "training-sword",
            ["SwordMaster:PlayerAssets:DevelopmentSeed:BaseAttack"] = "10",
            ["SwordMaster:PlayerAssets:DevelopmentSeed:BaseHealth"] = "100"
        };
    }

    private void SetEnvironmentVariable(string name, string? value)
    {
        _previousEnvironmentVariables.TryAdd(
            name,
            Environment.GetEnvironmentVariable(name));
        Environment.SetEnvironmentVariable(name, value);
    }

    private void RestoreEnvironment()
    {
        if (_environmentRestored)
        {
            return;
        }

        foreach (var (name, value) in _previousEnvironmentVariables)
        {
            Environment.SetEnvironmentVariable(name, value);
        }

        _environmentRestored = true;
    }
}
