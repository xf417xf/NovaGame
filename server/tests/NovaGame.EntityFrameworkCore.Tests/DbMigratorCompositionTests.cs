using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NovaGame.Application.Auditing;
using NovaGame.DbMigrator;
using NovaGame.DbMigrator.Auditing;
using Xunit;

namespace NovaGame.EntityFrameworkCore.Tests;

public sealed class DbMigratorCompositionTests
{
    [Fact]
    public void ModuleRegistersNonHttpRequestAuditContext()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        new NovaGameDbMigratorModule().ConfigureServices(services, configuration);

        using var serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        using var scope = serviceProvider.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IRequestAuditContextAccessor>();

        Assert.IsType<DbMigratorRequestAuditContextAccessor>(accessor);
        Assert.Equal(new RequestAuditContext(null, null, null), accessor.Current);
    }
}
