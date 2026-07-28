using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace NovaGame.EntityFrameworkCore.Tests;

internal sealed class NovaGameApiFactory : WebApplicationFactory<Program>
{
    internal const string JwtIssuer = "NovaGame.Tests";
    internal const string JwtAudience = "NovaGame.Admin.Tests";
    internal const string JwtSigningKey = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY";

    private readonly string _connectionString;
    private readonly string _seedUserName;
    private readonly string _seedPassword;
    private readonly int _loginPermitLimit;

    public NovaGameApiFactory(
        string connectionString,
        string seedUserName = "admin",
        string seedPassword = "Test-Administrator-Password-123!",
        int loginPermitLimit = 100)
    {
        _connectionString = connectionString;
        _seedUserName = seedUserName;
        _seedPassword = seedPassword;
        _loginPermitLimit = loginPermitLimit;
    }

    public HttpClient CreateHttpsClient(bool handleCookies = true)
    {
        return CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = handleCookies
        });
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", _connectionString);
        builder.UseSetting("NovaGame:EntityFrameworkCore:ServerVersion", "8.4.0");
        builder.UseSetting("NovaGame:AdminAccess:Jwt:Issuer", JwtIssuer);
        builder.UseSetting("NovaGame:AdminAccess:Jwt:Audience", JwtAudience);
        builder.UseSetting("NovaGame:AdminAccess:Jwt:SigningKey", JwtSigningKey);
        builder.UseSetting("NovaGame:AdminAccess:Jwt:ClockSkew", "00:00:00");
        builder.UseSetting("NovaGame:AdminAccess:LoginRateLimit:PermitLimit", _loginPermitLimit.ToString());
        builder.UseSetting("NovaGame:AdminAccess:LoginRateLimit:Window", "00:01:00");
        builder.UseSetting("NovaGame:AdminAccess:LoginRateLimit:QueueLimit", "0");
        builder.UseSetting("NovaGame:AdminAccess:Seed:UserName", _seedUserName);
        builder.UseSetting("NovaGame:AdminAccess:Seed:Password", _seedPassword);
        builder.UseSetting("NovaGame:AdminAccess:Seed:DisplayName", "Test Administrator");
        builder.UseSetting("NovaCore:Web:ForwardedHeaders:IsEnabled", "true");
        builder.UseSetting("NovaCore:Web:ForwardedHeaders:KnownProxies:0", "127.0.0.1");
    }
}
