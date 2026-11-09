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
/// Niveau 3 – asynkron event-testing af LagerService som CONSUMER.
///
/// Testen spiller selv OrderService og sender et OrderCreatedIntegrationEvent til en ægte
/// RabbitMQ (opgave 3A). LagerService' OrderEventsConsumer skal så trække varerne fra lageret.
/// Det sker i baggrunden, så lageret tjekkes med polling (opgave 3B).
/// </summary>
[Collection(RabbitMqCollection.Name)]
[Trait("Category", "RabbitMQ")]
public class OrderEventsConsumingTests : IAsyncLifetime
{
    // Samme JSON-indstillinger (camelCase) som OrderService bruger, når den sender.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly RabbitMqContainerFixture _rabbitMq;
    private readonly LagerServiceWithRabbitMqApplication _application;
    private HttpClient _client = null!;

    // Testens egen forbindelse, som den bruger til at publicere events.
    private IConnection _connection = null!;
    private IChannel _channel = null!;

    public OrderEventsConsumingTests(RabbitMqContainerFixture rabbitMq)
    {
        _rabbitMq = rabbitMq;
        _application = new LagerServiceWithRabbitMqApplication(rabbitMq);
    }

    /// <summary>Kører før hver test.</summary>
    public async Task InitializeAsync()
    {
        // CreateClient starter LagerService, og dermed også consumeren (en BackgroundService).
        _client = _application.CreateClient();

        var factory = new ConnectionFactory { Uri = new Uri(_rabbitMq.ConnectionString) };
        _connection = await factory.CreateConnectionAsync();
        _channel = await _connection.CreateChannelAsync();

        // Consumeren forbinder i baggrunden og har måske ikke nået at oprette sin kø endnu.
        // Hvis vi sender, før køen findes, smider fanout-exchangen beskeden væk.
        // Derfor opretter og binder testen den SAMME kø med de samme indstillinger (det er
        // idempotent). Så venter beskederne i køen, til consumeren er klar.
        await _channel.ExchangeDeclareAsync(OrderEventsExchange.Name, ExchangeType.Fanout, durable: true);
        await _channel.QueueDeclareAsync(_application.QueueName, durable: true, exclusive: false, autoDelete: false);
        await _channel.QueueBindAsync(_application.QueueName, OrderEventsExchange.Name, routingKey: string.Empty);
    }

    /// <summary>Kører efter hver test: sletter testens kø, lukker forbindelsen og stopper LagerService.</summary>
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
        // Arrange: læg 10 stk. af en vare på lager. Et tilfældigt vare-id sikrer,
        // at testen ikke rammer data fra en anden test.
        var catalogItemId = Random.Shared.Next(100_000, 200_000);
        await _client.PutAsJsonAsync($"/api/stock/{catalogItemId}", new SetQuantityRequest(10));

        // Act: send et event om en ordre på 3 stk., præcis som OrderService ville gøre det.
        await PublishAsync(OrderCreated(catalogItemId, units: 3));

        // Assert (opgave 3B): spørg lageret igen og igen, til det er faldet til 7 eller tiden løber ud.
        var quantity = await EventuallyGetQuantityAsync(catalogItemId, expected: 7);
        quantity.Should().Be(7);
    }

    [Fact]
    public async Task UnreadableMessageIsDroppedAndDoesNotChangeTheStock()
    {
        // Arrange
        var catalogItemId = Random.Shared.Next(200_000, 300_000);
        await _client.PutAsJsonAsync($"/api/stock/{catalogItemId}", new SetQuantityRequest(10));

        // Act: først en ødelagt besked, så en gyldig ordre på 2 stk.
        // Man kan ikke polle på, at noget IKKE sker. Tricket er, at consumeren tager én besked
        // ad gangen (prefetchCount: 1). Når den gyldige besked er behandlet, er den ødelagte
        // altså også blevet håndteret. Så behøver testen ikke vente et fast antal sekunder.
        await PublishAsync(Encoding.UTF8.GetBytes("this is not json"));
        await PublishAsync(OrderCreated(catalogItemId, units: 2));

        // Assert: kun den gyldige ordre har ændret lageret (10 - 2 = 8).
        // Den ødelagte besked er smidt væk og ikke lagt tilbage i køen igen og igen.
        var quantity = await EventuallyGetQuantityAsync(catalogItemId, expected: 8);
        quantity.Should().Be(8);
    }

    /// <summary>
    /// Opgave 3B: henter lagerantallet via LagerService' API, indtil det er det forventede,
    /// eller Polly har brugt sine forsøg. Returnerer det sidst sete antal, så testen kan
    /// vise et brugbart fejlbillede ("forventede 7, men var 10").
    /// </summary>
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

    /// <summary>Bygger et OrderCreatedIntegrationEvent med én vare og laver det om til JSON-bytes.</summary>
    private static byte[] OrderCreated(int catalogItemId, int units)
    {
        // Nyt EventId hver gang, ellers ville LagerService springe det over som en dublet.
        var orderCreated = new OrderCreatedIntegrationEvent(
            Guid.NewGuid(), DateTimeOffset.UtcNow, OrderId: 1, BuyerId: "buyer", Total: 10m * units,
            new List<OrderItemDto> { new(catalogItemId, "Product", "", 10m, units) });
        return JsonSerializer.SerializeToUtf8Bytes(orderCreated, JsonOptions);
    }

    /// <summary>
    /// Sender en besked på samme måde som RabbitMqOrderEventPublisher i OrderService:
    /// til fanout-exchangen "order-events", med eventets navn i Type.
    /// </summary>
    private async Task PublishAsync(byte[] body)
    {
        var properties = new BasicProperties
        {
            MessageId = Guid.NewGuid().ToString(),
            ContentType = "application/json",
            // Consumeren ser på Type for at vide, hvilken slags event det er.
            Type = nameof(OrderCreatedIntegrationEvent),
            // Gemmes på disk i RabbitMQ, ligesom de rigtige ordre-events.
            Persistent = true
        };
        await _channel.BasicPublishAsync(OrderEventsExchange.Name, routingKey: string.Empty, mandatory: false,
            basicProperties: properties, body: body);
    }
}
