using System.Net;
using System.Text.Json;
using Xunit;

namespace NovaGame.EntityFrameworkCore.Tests;

[Collection(MySqlDatabaseCollection.Name)]
public sealed class MySqlHealthEndpointTests
{
    private const string DatabaseCheckName =
        "database:NovaGame.Host.EntityFrameworkCore.NovaGameDbContext";

    private readonly MySqlDatabaseFixture _fixture;

    public MySqlHealthEndpointTests(MySqlDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData("/health/ready")]
    [InlineData("/health")]
    [InlineData("/api/health")]
    public async Task HealthEndpointReportsMySqlAsHealthy(string path)
    {
        await using var factory = new NovaGameApiFactory(_fixture.ConnectionString);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var body = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(body);
        var mysql = document.RootElement
            .GetProperty("checks")
            .GetProperty(DatabaseCheckName);

        Assert.Equal("Healthy", mysql.GetProperty("status").GetString());
    }

    [Fact]
    public async Task LivenessEndpointDoesNotDependOnMySql()
    {
        await using var factory = new NovaGameApiFactory(_fixture.ConnectionString);
        using var client = factory.CreateHttpsClient();

        using var response = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var body = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(body);
        var checks = document.RootElement.GetProperty("checks");
        var check = Assert.Single(checks.EnumerateObject());
        Assert.Equal("self", check.Name);
        Assert.Equal("Healthy", check.Value.GetProperty("status").GetString());
    }

    [Fact]
    public async Task ReadinessEndpointsReportUnavailableMySqlWithoutLeakingCredentials()
    {
        const string password = "health-check-test-password";
        var connectionString =
            $"Server=127.0.0.1;Port=1;Database=missing;User ID=missing;Password={password};" +
            "Connection Timeout=1;SslMode=None;AllowPublicKeyRetrieval=True";
        await using var factory = new NovaGameApiFactory(connectionString);
        using var client = factory.CreateHttpsClient();

        using (var liveResponse = await client.GetAsync("/health/live"))
        {
            Assert.Equal(HttpStatusCode.OK, liveResponse.StatusCode);
        }

        foreach (var path in new[] { "/health/ready", "/health", "/api/health" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

            var responseBody = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(responseBody);
            var mysql = document.RootElement
                .GetProperty("checks")
                .GetProperty(DatabaseCheckName);

            Assert.Equal("Unhealthy", mysql.GetProperty("status").GetString());
            Assert.DoesNotContain(password, responseBody, StringComparison.Ordinal);
        }
    }
}
