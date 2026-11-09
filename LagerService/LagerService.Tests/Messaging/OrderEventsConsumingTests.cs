using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.eShopWeb.LagerService.Endpoints;
using Microsoft.eShopWeb.OrderService.Contracts;
using Microsoft.eShopWeb.OrderService.Contracts.Events;
using RabbitMQ.Client;
using Xunit;

namespace Microsoft.eShopWeb.LagerService.Tests.Messaging;

/// <summary>
/// Niveau 3: an OrderCreatedIntegrationEvent published on a real RabbitMQ (3A) must take the items out of
/// LagerService's stock. The consumer works in the background, so the stock is polled (3B).
/// </summary>
[Collection(RabbitMqCollection.Name)]
[Trait("Category", "RabbitMQ")]
public class OrderEventsConsumingTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly RabbitMqContainerFixture _rabbitMq;
    private readonly LagerServiceWithRabbitMqApplication _application;
    private HttpClient _client = null!;
    private IConnection _connection = null!;
    private IChannel _channel = null!;

    public OrderEventsConsumingTests(RabbitMqContainerFixture rabbitMq)
    {
        _rabbitMq = rabbitMq;
        _application = new LagerServiceWithRabbitMqApplication(rabbitMq);
    }

    public async Task InitializeAsync()
    {
        // Starts LagerService, and with it the consumer.
        _client = _application.CreateClient();

        var factory = new ConnectionFactory { Uri = new Uri(_rabbitMq.ConnectionString) };
        _connection = await factory.CreateConnectionAsync();
        _channel = await _connection.CreateChannelAsync();

        // The consumer connects in the background and may not have bound its queue yet. Declaring the same
        // queue and binding here (idempotent) makes sure the messages wait in it instead of being dropped.
        await _channel.ExchangeDeclareAsync(OrderEventsExchange.Name, ExchangeType.Fanout, durable: true);
        await _channel.QueueDeclareAsync(_application.QueueName, durable: true, exclusive: false, autoDelete: false);
        await _channel.QueueBindAsync(_application.QueueName, OrderEventsExchange.Name, routingKey: string.Empty);
    }

    public async Task DisposeAsync()
    {
        await _channel.QueueDeleteAsync(_application.QueueName);
        await _channel.DisposeAsync();
        await _connection.DisposeAsync();
        await _application.DisposeAsync();
    }

    [Fact]
    public async Task OrderCreatedTakesTheItemsOutOfStock()
    {
        // Arrange
        var catalogItemId = Random.Shared.Next(100_000, 200_000);
        await _client.PutAsJsonAsync($"/api/stock/{catalogItemId}", new SetQuantityRequest(10));

        // Act
        await PublishAsync(OrderCreated(catalogItemId, units: 3));

        // Assert
        var quantity = await EventuallyGetQuantityAsync(catalogItemId, expected: 7);
        quantity.Should().Be(7);
    }

    [Fact]
    public async Task UnreadableMessageIsDroppedAndDoesNotChangeTheStock()
    {
        // Arrange
        var catalogItemId = Random.Shared.Next(200_000, 300_000);
        await _client.PutAsJsonAsync($"/api/stock/{catalogItemId}", new SetQuantityRequest(10));

        // Act: a broken message first, then a valid one. The consumer takes one message at a time,
        // so once the valid one is handled the broken one has been dealt with too. No fixed wait needed.
        await PublishAsync(Encoding.UTF8.GetBytes("this is not json"));
        await PublishAsync(OrderCreated(catalogItemId, units: 2));

        // Assert: only the valid order counted
        var quantity = await EventuallyGetQuantityAsync(catalogItemId, expected: 8);
        quantity.Should().Be(8);
    }

    private async Task<int?> EventuallyGetQuantityAsync(int catalogItemId, int expected)
    {
        int? quantity = null;
        await Eventually.TrueAsync(async () =>
        {
            var item = await _client.GetFromJsonAsync<StockItemDto>($"/api/stock/{catalogItemId}");
            quantity = item?.Quantity;
            return quantity == expected;
        });
        return quantity;
    }

    private static byte[] OrderCreated(int catalogItemId, int units)
    {
        var orderCreated = new OrderCreatedIntegrationEvent(
            Guid.NewGuid(), DateTimeOffset.UtcNow, OrderId: 1, BuyerId: "buyer", Total: 10m * units,
            new List<OrderItemDto> { new(catalogItemId, "Product", "", 10m, units) });
        return JsonSerializer.SerializeToUtf8Bytes(orderCreated, JsonOptions);
    }

    /// <summary>Publishes like OrderService does: to the fanout exchange, with the event name in Type.</summary>
    private async Task PublishAsync(byte[] body)
    {
        var properties = new BasicProperties
        {
            MessageId = Guid.NewGuid().ToString(),
            ContentType = "application/json",
            Type = nameof(OrderCreatedIntegrationEvent),
            Persistent = true
        };
        await _channel.BasicPublishAsync(OrderEventsExchange.Name, routingKey: string.Empty, mandatory: false,
            basicProperties: properties, body: body);
    }
}
