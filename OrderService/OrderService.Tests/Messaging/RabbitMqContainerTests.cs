using FluentAssertions;
using RabbitMQ.Client;
using Xunit;

namespace Microsoft.eShopWeb.OrderService.Tests.Messaging;

/// <summary>
/// Opgave 3A: smoke test that the container started and the connection details work.
/// </summary>
[Collection(RabbitMqCollection.Name)]
[Trait("Category", "RabbitMQ")]
public class RabbitMqContainerTests
{
    private readonly RabbitMqContainerFixture _rabbitMq;

    public RabbitMqContainerTests(RabbitMqContainerFixture rabbitMq)
    {
        _rabbitMq = rabbitMq;
    }

    [Fact]
    public async Task CanConnectToTheContainer()
    {
        var factory = new ConnectionFactory { Uri = new Uri(_rabbitMq.ConnectionString) };

        await using var connection = await factory.CreateConnectionAsync();

        connection.IsOpen.Should().BeTrue();
    }
}
