using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.eShopWeb.OrderService.Contracts;
using Microsoft.eShopWeb.OrderService.Contracts.Events;
using RabbitMQ.Client;
using Xunit;

namespace Microsoft.eShopWeb.OrderService.Tests.Messaging;

/// <summary>
/// Niveau 3: creating an order through the API must put an OrderCreatedIntegrationEvent on the
/// "order-events" exchange in a real RabbitMQ (3A), checked with polling because it is asynchronous (3B).
/// </summary>
[Collection(RabbitMqCollection.Name)]
[Trait("Category", "RabbitMQ")]
public class OrderCreatedPublishingTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly RabbitMqContainerFixture _rabbitMq;
    private readonly OrderServiceWithRabbitMqApplication _application;
    private IConnection _connection = null!;
    private IChannel _channel = null!;
    private string _queueName = null!;

    public OrderCreatedPublishingTests(RabbitMqContainerFixture rabbitMq)
    {
        _rabbitMq = rabbitMq;
        _application = new OrderServiceWithRabbitMqApplication(rabbitMq);
    }

    public async Task InitializeAsync()
    {
        // A fanout exchange throws messages away when no queue is bound, so the test binds its own
        // queue BEFORE the order is created. Server-named and exclusive: no other test sees it.
        var factory = new ConnectionFactory { Uri = new Uri(_rabbitMq.ConnectionString) };
        _connection = await factory.CreateConnectionAsync();
        _channel = await _connection.CreateChannelAsync();
        await _channel.ExchangeDeclareAsync(OrderEventsExchange.Name, ExchangeType.Fanout, durable: true);
        var queue = await _channel.QueueDeclareAsync(queue: string.Empty, durable: false, exclusive: true, autoDelete: true);
        _queueName = queue.QueueName;
        await _channel.QueueBindAsync(_queueName, OrderEventsExchange.Name, routingKey: string.Empty);
    }

    public async Task DisposeAsync()
    {
        await _channel.DisposeAsync();
        await _connection.DisposeAsync();
        await _application.DisposeAsync();
    }

    [Fact]
    public async Task CreatingAnOrderPublishesOrderCreatedToRabbitMq()
    {
        // Arrange
        var client = _application.CreateClient();
        var buyerId = Guid.NewGuid().ToString();
        var request = new CreateOrderRequest(
            buyerId,
            new AddressDto("123 Main St.", "Kent", "OH", "United States", "44240"),
            new List<CreateOrderItemDto>
            {
                new(1, ".NET Bot Black Sweatshirt", "http://catalogbaseurltobereplaced/images/products/1.png", 19.5m, 2)
            });

        // Act
        var response = await client.PostAsJsonAsync("/api/orders", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<CreateOrderResponse>();

        // Assert: keep reading the queue until our event shows up (3B)
        OrderCreatedIntegrationEvent? received = null;
        var found = await Eventually.TrueAsync(async () =>
        {
            received = await TryReadOrderCreatedAsync();
            return received?.OrderId == created!.OrderId;
        });

        found.Should().BeTrue("OrderService should publish OrderCreated within {0} attempts", Eventually.Retries + 1);
        received!.BuyerId.Should().Be(buyerId);
        received.Total.Should().Be(19.5m * 2);
        received.Items.Should().ContainSingle(i => i.CatalogItemId == 1 && i.Units == 2);
    }

    private async Task<OrderCreatedIntegrationEvent?> TryReadOrderCreatedAsync()
    {
        var message = await _channel.BasicGetAsync(_queueName, autoAck: true);
        if (message is null || message.BasicProperties.Type != nameof(OrderCreatedIntegrationEvent))
        {
            return null;
        }
        return JsonSerializer.Deserialize<OrderCreatedIntegrationEvent>(message.Body.Span, JsonOptions);
    }
}
