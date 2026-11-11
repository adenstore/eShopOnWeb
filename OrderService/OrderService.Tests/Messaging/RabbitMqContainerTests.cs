using FluentAssertions;
using RabbitMQ.Client;
using Xunit;

namespace Microsoft.eShopWeb.OrderService.Tests.Messaging;

/// <summary>
/// Opgave 3A – smoke test af infrastrukturen.
/// Hvis denne test fejler, ligger problemet i Docker eller containeren og ikke i OrderService.
/// </summary>
// Deler RabbitMQ-containeren med de andre testklasser i samme collection.
[Collection(RabbitMqCollection.Name)]
// Gør det muligt at køre (eller springe over) de langsomme tests med: dotnet test --filter Category=RabbitMQ
[Trait("Category", "RabbitMQ")]
public class RabbitMqContainerTests
{
    private readonly RabbitMqContainerFixture _rabbitMq;

    // xUnit giver automatisk den fælles fixture med i konstruktøren.
    public RabbitMqContainerTests(RabbitMqContainerFixture rabbitMq)
    {
        _rabbitMq = rabbitMq;
    }

    [Fact]
    public async Task CanConnectToTheContainer()
    {
        // Arrange: brug forbindelsesstrengen, som containeren selv har givet os.
        var factory = new ConnectionFactory { Uri = new Uri(_rabbitMq.ConnectionString) };

        // Act: åbn en forbindelse til RabbitMQ.
        await using var connection = await factory.CreateConnectionAsync();

        // Assert: forbindelsen er åben, altså kører RabbitMQ og oplysningerne er korrekte.
        connection.IsOpen.Should().BeTrue();
    }
}
