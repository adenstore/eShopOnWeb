using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Microsoft.eShopWeb.OrderService.Tests.Messaging;

/// <summary>
/// Opgave 3A – injicér containerens forbindelsesoplysninger i mikrotjenesten.
///
/// Starter OrderService i hukommelsen ligesom <see cref="Endpoints.OrderServiceApplication"/>,
/// med én vigtig forskel: den udskifter IKKE RabbitMqOrderEventPublisher med LoggingOrderEventPublisher.
/// Den rigtige publisher bliver brugt og sender til test-containeren.
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

        // Overskriver værdierne fra appsettings.json. OrderService binder sektionen "RabbitMQ"
        // til RabbitMqSettings, så selve servicen skal ikke ændres for at kunne testes.
        builder.ConfigureHostConfiguration(config =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Databasen kører stadig i hukommelsen, så der skal ikke bruges SQL Server.
                ["UseOnlyInMemoryDatabase"] = "true",
                // Peg publisheren på containeren i stedet for localhost:5672.
                ["RabbitMQ:HostName"] = _rabbitMq.HostName,
                ["RabbitMQ:Port"] = _rabbitMq.Port.ToString(),
                ["RabbitMQ:UserName"] = RabbitMqContainerFixture.UserName,
                ["RabbitMQ:Password"] = RabbitMqContainerFixture.Password
            }));

        return base.CreateHost(builder);
    }
}
