using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Microsoft.eShopWeb.LagerService.Tests;

/// <summary>
/// Runs LagerService in memory: no Redis and no RabbitMQ needed.
/// </summary>
public class LagerServiceApplication : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureHostConfiguration(config =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["UseOnlyInMemoryDatabase"] = "true",
                ["RabbitMQ:Enabled"] = "false"
            }));

        return base.CreateHost(builder);
    }
}
