using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Microsoft.eShopWeb.LagerService.Tests.Messaging;

/// <summary>
/// Opgave 3A: like <see cref="LagerServiceApplication"/>, but with the OrderEventsConsumer turned on and
/// pointed at the test container. Each instance gets its own queue, so tests do not steal each other's messages.
/// The stock still lives in memory, so Redis is not needed.
/// </summary>
public class LagerServiceWithRabbitMqApplication : WebApplicationFactory<Program>
{
    private readonly RabbitMqContainerFixture _rabbitMq;

    public LagerServiceWithRabbitMqApplication(RabbitMqContainerFixture rabbitMq)
    {
        _rabbitMq = rabbitMq;
    }

    public string QueueName { get; } = $"lager-service-test.{Guid.NewGuid():N}";

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureHostConfiguration(config =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["UseOnlyInMemoryDatabase"] = "true",
                ["RabbitMQ:Enabled"] = "true",
                ["RabbitMQ:HostName"] = _rabbitMq.HostName,
                ["RabbitMQ:Port"] = _rabbitMq.Port.ToString(),
                ["RabbitMQ:UserName"] = RabbitMqContainerFixture.UserName,
                ["RabbitMQ:Password"] = RabbitMqContainerFixture.Password,
                ["RabbitMQ:QueueName"] = QueueName
            }));

        return base.CreateHost(builder);
    }
}
