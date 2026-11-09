using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Microsoft.eShopWeb.OrderService.Tests.Messaging;

/// <summary>
/// Opgave 3A: like <see cref="Endpoints.OrderServiceApplication"/>, but keeps the real RabbitMqOrderEventPublisher
/// and points it at the test container by overriding the "RabbitMQ" settings. The service itself is unchanged.
/// </summary>
public class OrderServiceWithRabbitMqApplication : WebApplicationFactory<Program>
{
    private readonly RabbitMqContainerFixture _rabbitMq;

    public OrderServiceWithRabbitMqApplication(RabbitMqContainerFixture rabbitMq)
    {
        _rabbitMq = rabbitMq;
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureHostConfiguration(config =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["UseOnlyInMemoryDatabase"] = "true",
                ["RabbitMQ:HostName"] = _rabbitMq.HostName,
                ["RabbitMQ:Port"] = _rabbitMq.Port.ToString(),
                ["RabbitMQ:UserName"] = RabbitMqContainerFixture.UserName,
                ["RabbitMQ:Password"] = RabbitMqContainerFixture.Password
            }));

        return base.CreateHost(builder);
    }
}
