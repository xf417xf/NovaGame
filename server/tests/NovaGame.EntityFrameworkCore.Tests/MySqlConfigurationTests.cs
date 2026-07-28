using Microsoft.EntityFrameworkCore;
using NovaGame.Host.EntityFrameworkCore.DependencyInjection;
using Xunit;

namespace NovaGame.EntityFrameworkCore.Tests;

public sealed class MySqlConfigurationTests
{
    [Fact]
    public void KeepsProviderRetriesDisabledForNovaCoreTransactions()
    {
        var options = new DbContextOptionsBuilder<DbContext>();
        NovaGameEntityFrameworkCoreServiceCollectionExtensions.ConfigureMySql(
            options,
            "Server=127.0.0.1;Database=novagame;User ID=novagame;Password=unused",
            "8.4.0");

        using var dbContext = new DbContext(options.Options);

        Assert.False(dbContext.Database.CreateExecutionStrategy().RetriesOnFailure);
    }

    [Theory]
    [InlineData("8.0.36")]
    [InlineData("5.7.44")]
    [InlineData("9.0.0")]
    [InlineData("invalid")]
    public void RejectsUnsupportedServerVersions(string serverVersion)
    {
        var options = new DbContextOptionsBuilder();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            NovaGameEntityFrameworkCoreServiceCollectionExtensions.ConfigureMySql(
                options,
                "Server=127.0.0.1;Database=novagame;User ID=novagame;Password=unused",
                serverVersion));

        Assert.Contains("MySQL 8.4.x", exception.Message, StringComparison.Ordinal);
    }
}
