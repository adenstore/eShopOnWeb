using System.Text.Json;
using Microsoft.eShopWeb.BasketService.Contracts;
using Microsoft.eShopWeb.OrderService.Contracts;
using Microsoft.eShopWeb.OrderService.Contracts.Events;
using Xunit;

namespace Microsoft.eShopWeb.BasketService.Tests.Contracts;

/// <summary>
/// Every publisher and consumer in the repo serializes with JsonSerializerDefaults.Web (camelCase,
/// case-insensitive reads). These tests pin that down, because a message shape that does not
/// round-trip fails at runtime on a queue, where there is no compiler and no caller to see a 400.
/// </summary>
public class WireFormatTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void BasketChangedSurvivesARoundTrip()
    {
        var published = BasketChangedIntegrationEvent.For(new BasketSnapshot(7, "buyer@example.com",
            new[] { new BasketItemSnapshot(Id: 3, CatalogItemId: 2, UnitPrice: 3.40m, Quantity: 4) }));

        var received = JsonSerializer.Deserialize<BasketChangedIntegrationEvent>(
            JsonSerializer.SerializeToUtf8Bytes(published, JsonOptions), JsonOptions);

        Assert.NotNull(received);
        Assert.Equal(published.EventId, received!.EventId);
        Assert.Equal(published.OccurredAt, received.OccurredAt);
        Assert.Equal(7, received.Basket.BasketId);
        Assert.Equal("buyer@example.com", received.Basket.BuyerId);

        // The basket item id has to come back: it is the key SetQuantities is addressed by.
        var item = Assert.Single(received.Basket.Items);
        Assert.Equal(3, item.Id);
        Assert.Equal(3.40m, item.UnitPrice);
    }

    [Fact]
    public void CommandsSurviveARoundTrip()
    {
        var sent = new AddItemToBasketCommand
        {
            BuyerId = "buyer@example.com",
            CatalogItemId = 2,
            UnitPrice = 3.40m,
            Quantity = 4
        };

        var received = JsonSerializer.Deserialize<AddItemToBasketCommand>(
            JsonSerializer.SerializeToUtf8Bytes(sent, JsonOptions), JsonOptions);

        Assert.Equal(sent, received);
    }

    [Fact]
    public void SetQuantitiesKeepsItsItemKeyedMap()
    {
        var sent = new SetBasketQuantitiesCommand
        {
            BasketId = 4,
            Quantities = new Dictionary<string, int> { ["11"] = 2, ["12"] = 0 }
        };

        var received = JsonSerializer.Deserialize<SetBasketQuantitiesCommand>(
            JsonSerializer.SerializeToUtf8Bytes(sent, JsonOptions), JsonOptions);

        Assert.NotNull(received);
        Assert.Equal(2, received!.Quantities["11"]);
        Assert.Equal(0, received.Quantities["12"]);
    }

    [Fact]
    public void ANotFoundReplyIsAnAbsentBasketRatherThanAnError()
    {
        // Regression test: built positionally as new(null) this bound to the record's generated
        // copy constructor instead of the primary one, and threw on every miss.
        var reply = BasketQueryReply.NotFound();

        var received = JsonSerializer.Deserialize<BasketQueryReply>(
            JsonSerializer.SerializeToUtf8Bytes(reply, JsonOptions), JsonOptions);

        Assert.NotNull(received);
        Assert.Null(received!.Basket);
        Assert.Null(received.Error);
    }

    [Fact]
    public void OrderCreatedDeserializesExactlyAsOrderServicePublishesIt()
    {
        // Serialized here the same way RabbitMqOrderEventPublisher does it, from the same contracts
        // assembly, so this breaks if either side's shape moves.
        var published = new OrderCreatedIntegrationEvent(
            Guid.NewGuid(), DateTimeOffset.UtcNow, OrderId: 17, BuyerId: "buyer@example.com",
            Total: 13.60m, Items: new List<OrderItemDto> { new(2, "Product", "picture.png", 3.40m, 4) });

        var received = JsonSerializer.Deserialize<OrderCreatedIntegrationEvent>(
            JsonSerializer.SerializeToUtf8Bytes(published, JsonOptions), JsonOptions);

        Assert.NotNull(received);
        // BuyerId is the only field the basket side actually needs — it is how the basket to clear
        // is found — so it is the one that must not go missing.
        Assert.Equal("buyer@example.com", received!.BuyerId);
        Assert.Equal(17, received.OrderId);
    }
}
