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
/// Niveau 3 – asynkron event-testing af OrderService som PRODUCER.
///
/// Når en ordre oprettes via API'et, skal OrderService sende et OrderCreatedIntegrationEvent
/// til fanout-exchangen "order-events" i en ægte RabbitMQ (opgave 3A).
/// Fordi beskeden sendes asynkront, læser testen køen med polling (opgave 3B).
///
/// Forskel fra niveau 2 (OrderEndpointsTests): dér erstattes publisheren af en fake, så vi
/// ved kun, at API'et virker. Her beviser vi, at beskeden faktisk når frem til RabbitMQ.
/// </summary>
[Collection(RabbitMqCollection.Name)]
[Trait("Category", "RabbitMQ")]
public class OrderCreatedPublishingTests : IAsyncLifetime
{
    // Samme JSON-indstillinger (camelCase) som OrderService bruger, når den sender.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly RabbitMqContainerFixture _rabbitMq;
    private readonly OrderServiceWithRabbitMqApplication _application;

    // Testens egen forbindelse til RabbitMQ, som den bruger til at "lytte med" på exchangen.
    private IConnection _connection = null!;
    private IChannel _channel = null!;
    private string _queueName = null!;

    public OrderCreatedPublishingTests(RabbitMqContainerFixture rabbitMq)
    {
        _rabbitMq = rabbitMq;
        _application = new OrderServiceWithRabbitMqApplication(rabbitMq);
    }

    /// <summary>Kører før hver test (xUnit opretter en ny instans af klassen pr. test).</summary>
    public async Task InitializeAsync()
    {
        var factory = new ConnectionFactory { Uri = new Uri(_rabbitMq.ConnectionString) };
        _connection = await factory.CreateConnectionAsync();
        _channel = await _connection.CreateChannelAsync();

        // Opret exchangen på samme måde som OrderService gør (det er idempotent: sker kun første gang).
        await _channel.ExchangeDeclareAsync(OrderEventsExchange.Name, ExchangeType.Fanout, durable: true);

        // VIGTIGT: en fanout-exchange smider beskeder væk, hvis ingen kø er bundet til den.
        // Derfor opretter og binder testen sin egen kø, FØR ordren oprettes.
        // queue: "" = RabbitMQ finder selv på et unikt navn. exclusive/autoDelete = køen
        // forsvinder, når testens forbindelse lukkes, så andre tests ikke ser den.
        var queue = await _channel.QueueDeclareAsync(queue: string.Empty, durable: false, exclusive: true, autoDelete: true);
        _queueName = queue.QueueName;
        await _channel.QueueBindAsync(_queueName, OrderEventsExchange.Name, routingKey: string.Empty);
    }

    /// <summary>Kører efter hver test: lukker forbindelsen (og dermed køen) og stopper OrderService.</summary>
    public async Task DisposeAsync()
    {
        await _channel.DisposeAsync();
        await _connection.DisposeAsync();
        await _application.DisposeAsync();
    }

    [Fact]
    public async Task CreatingAnOrderPublishesOrderCreatedToRabbitMq()
    {
        // Arrange: start OrderService (med rigtig publisher) og lav en ordre med én vare.
        var client = _application.CreateClient();
        var buyerId = Guid.NewGuid().ToString();
        var request = new CreateOrderRequest(
            buyerId,
            new AddressDto("123 Main St.", "Kent", "OH", "United States", "44240"),
            new List<CreateOrderItemDto>
            {
                new(1, ".NET Bot Black Sweatshirt", "http://catalogbaseurltobereplaced/images/products/1.png", 19.5m, 2)
            });

        // Act: opret ordren via API'et. Svaret kommer, før vi ved om eventet er nået frem.
        var response = await client.PostAsJsonAsync("/api/orders", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<CreateOrderResponse>();

        // Assert (opgave 3B): læs køen igen og igen, indtil vores event dukker op eller tiden løber ud.
        // "received" gemmes udenfor lambdaen, så vi kan tjekke indholdet bagefter.
        OrderCreatedIntegrationEvent? received = null;
        var found = await Eventually.TrueAsync(async () =>
        {
            received = await TryReadOrderCreatedAsync();
            return received?.OrderId == created!.OrderId;
        });
        
        // Eventet skal være kommet, og indholdet skal passe med den ordre, vi oprettede.
        found.Should().BeTrue("OrderService should publish OrderCreated within {0} attempts", Eventually.Retries + 1);
        received!.BuyerId.Should().Be(buyerId);
        received.Total.Should().Be(19.5m * 2);
        received.Items.Should().ContainSingle(i => i.CatalogItemId == 1 && i.Units == 2);
    }

    /// <summary>
    /// Henter ÉN besked fra testens kø, hvis der er en, og laver den om til et OrderCreatedIntegrationEvent.
    /// Returnerer null, hvis køen er tom eller beskeden er af en anden type. Så returnerer
    /// lambdaen ovenfor false, og Polly venter et sekund og prøver igen.
    /// </summary>
    private async Task<OrderCreatedIntegrationEvent?> TryReadOrderCreatedAsync()
    {
        // BasicGet "trækker" én besked (pull) i stedet for at abonnere med en consumer.
        // Den venter ikke: er køen tom, kommer null tilbage med det samme.
        // autoAck: true = beskeden fjernes fra køen, så snart den er hentet.
        var message = await _channel.BasicGetAsync(_queueName, autoAck: true);

        // Publisheren skriver eventets navn i Type, så modtagere kan kende beskederne fra hinanden.
        if (message is null || message.BasicProperties.Type != nameof(OrderCreatedIntegrationEvent))
        {
            return null;
        }
        return JsonSerializer.Deserialize<OrderCreatedIntegrationEvent>(message.Body.Span, JsonOptions);
    }
}
