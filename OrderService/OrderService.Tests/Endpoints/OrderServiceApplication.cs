using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.OrderService.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Microsoft.eShopWeb.OrderService.Tests.Endpoints;

public class OrderServiceApplication : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureHostConfiguration(config =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["UseOnlyInMemoryDatabase"] = "true"
            }));
        // The tests should not need a running RabbitMQ, so events are only logged.
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IOrderEventPublisher>();
            services.AddScoped<IOrderEventPublisher, LoggingOrderEventPublisher>();
        });

        return base.CreateHost(builder);
    }
}
