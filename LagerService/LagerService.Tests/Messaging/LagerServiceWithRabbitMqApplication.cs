using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Microsoft.eShopWeb.LagerService.Tests.Messaging;

/// <summary>
/// Opgave 3A – injicér containerens forbindelsesoplysninger i LagerService.
///
/// Starter LagerService i hukommelsen ligesom <see cref="LagerServiceApplication"/>, men med
/// OrderEventsConsumer slået TIL og peget på test-containeren. Lageret ligger stadig i
/// hukommelsen, så Redis er ikke nødvendig.
/// </summary>
public class LagerServiceWithRabbitMqApplication : WebApplicationFactory<Program>
{
    private readonly RabbitMqContainerFixture _rabbitMq;

    public LagerServiceWithRabbitMqApplication(RabbitMqContainerFixture rabbitMq)
    {
        _rabbitMq = rabbitMq;
    }

    /// <summary>
    /// Hver testkørsel får sin egen kø. Ellers kunne en besked fra én test blive
    /// "stjålet" af en anden tests consumer, fordi de deler samme container.
    /// </summary>
    public string QueueName { get; } = $"lager-service-test.{Guid.NewGuid():N}";

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // Overskriver appsettings.json. LagerService binder sektionen "RabbitMQ" til RabbitMqSettings.
        builder.ConfigureHostConfiguration(config =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Lageret i hukommelsen i stedet for Redis.
                ["UseOnlyInMemoryDatabase"] = "true",
                // De almindelige tests slår RabbitMQ fra; her skal consumeren køre.
                ["RabbitMQ:Enabled"] = "true",
                // Peg consumeren på containeren i stedet for localhost:5672.
                ["RabbitMQ:HostName"] = _rabbitMq.HostName,
                ["RabbitMQ:Port"] = _rabbitMq.Port.ToString(),
                ["RabbitMQ:UserName"] = RabbitMqContainerFixture.UserName,
                ["RabbitMQ:Password"] = RabbitMqContainerFixture.Password,
                ["RabbitMQ:QueueName"] = QueueName
            }));

        return base.CreateHost(builder);
    }
}
