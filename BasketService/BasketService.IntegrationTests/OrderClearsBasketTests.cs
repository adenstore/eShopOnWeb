using Microsoft.eShopWeb.BasketService.Contracts;
using Microsoft.eShopWeb.OrderService.Contracts.Events;
using Xunit;

namespace Microsoft.eShopWeb.BasketService.IntegrationTests;

/// <summary>
/// Service-to-service: OrderService publishes to the "order-events" fanout and BasketService
/// reacts. This is the flow that replaced the old SetQuantities → CreateOrder → DeleteBasket
/// sequence, where a crash between the calls could leave an order placed and the basket still full.
/// <para>
/// Nothing here calls BasketService directly — the only input is an event published to an exchange,
/// exactly as the order service would, which is what makes this an integration test of the
/// subscription rather than of a method.
/// </para>
/// </summary>
[Collection(BasketServiceCollection.Name)]
public class OrderClearsBasketTests : IAsyncLifetime
{
    private readonly BasketServiceApplication _app;
    private BasketMessageClient _client = null!;
    private readonly string _buyer = $"buyer-{Guid.NewGuid()}@test.com";

    public OrderClearsBasketTests(BasketServiceApplication app) => _app = app;

    public async Task InitializeAsync() => _client = await BasketMessageClient.ConnectAsync(_app);

    public async Task DisposeAsync() => await _client.DisposeAsync();

    private object OrderCreated(int orderId = 4242) => new
    {
        eventId = Guid.NewGuid(),
        occurredAt = DateTimeOffset.UtcNow,
        orderId,
        buyerId = _buyer,
        total = 13.60m,
        items = new[]
        {
            new { catalogItemId = 2, productName = "Mug", pictureUri = "mug.png", unitPrice = 3.40m, units = 4 }
        }
    };

    private async Task GivenABasketWithItemsAsync()
    {
        await _client.SendAsync(BasketMessaging.RoutingKeys.AddItem,
            new { buyerId = _buyer, catalogItemId = 2, unitPrice = 3.40m, quantity = 4 });

        var ready = await PolledWait.UntilAsync(async () =>
            (await _client.GetBasketByBuyerAsync(_buyer))?.Basket?.TotalItems == 4);
        Assert.True(ready, "could not set up a basket to clear");
    }

    [Fact]
    public async Task PlacingAnOrderEmptiesTheBuyersBasket()
    {
        await GivenABasketWithItemsAsync();

        await _client.PublishOrderEventAsync(OrderCreated(), nameof(OrderCreatedIntegrationEvent));

        var cleared = await PolledWait.UntilAsync(async () =>
        {
            var reply = await _client.GetBasketByBuyerAsync(_buyer);
            return reply?.Basket is null || reply.Basket.Items.Count == 0;
        });

        Assert.True(cleared, "the basket was never cleared after the order event");
    }

    [Fact]
    public async Task ClearingTheBasketIsAnnouncedAsAnEmptyBasket()
    {
        await GivenABasketWithItemsAsync();
        _client.ForgetObservedEvents();

        await _client.PublishOrderEventAsync(OrderCreated(), nameof(OrderCreatedIntegrationEvent));

        // A deleted basket is announced as full state with no items, so a reader needs no separate
        // "deleted" case — it applies the snapshot like any other.
        var announced = await PolledWait.UntilAsync(() =>
            Task.FromResult(_client.LatestEventFor(_buyer) is { Basket.Items.Count: 0 }));

        Assert.True(announced, "no empty BasketChanged announced the clear");
    }

    [Fact]
    public async Task TheSameOrderArrivingTwiceIsHarmless()
    {
        // Redelivery is normal after a broker or consumer restart, and is the reason clearing on an
        // event is safe where the old three-call checkout was not.
        await GivenABasketWithItemsAsync();
        var deadLetteredBefore = await _client.QueueDepthAsync(BasketMessaging.InvalidQueue);

        await _client.PublishOrderEventAsync(OrderCreated(), nameof(OrderCreatedIntegrationEvent));
        var cleared = await PolledWait.UntilAsync(async () =>
            (await _client.GetBasketByBuyerAsync(_buyer))?.Basket is null);
        Assert.True(cleared, "the basket was never cleared");

        await _client.PublishOrderEventAsync(OrderCreated(), nameof(OrderCreatedIntegrationEvent));

        // Give the repeat time to be mishandled, if it is going to be.
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Equal(deadLetteredBefore, await _client.QueueDepthAsync(BasketMessaging.InvalidQueue));
    }

    [Fact]
    public async Task AnOrderEventForSomeoneElseLeavesThisBasketAlone()
    {
        await GivenABasketWithItemsAsync();

        await _client.PublishOrderEventAsync(new
        {
            eventId = Guid.NewGuid(),
            occurredAt = DateTimeOffset.UtcNow,
            orderId = 99,
            buyerId = $"someone-else-{Guid.NewGuid()}",
            total = 1m,
            items = Array.Empty<object>()
        }, nameof(OrderCreatedIntegrationEvent));

        await Task.Delay(TimeSpan.FromSeconds(2));

        var reply = await _client.GetBasketByBuyerAsync(_buyer);
        Assert.Equal(4, reply!.Basket!.TotalItems);
    }

    [Fact]
    public async Task AnUnrelatedOrderEventIsIgnoredRatherThanDeadLettered()
    {
        // The fanout carries every order event. Ones this service does not handle are somebody
        // else's message, not a failure — dead-lettering them would fill the invalid queue with
        // noise.
        var before = await _client.QueueDepthAsync(BasketMessaging.InvalidQueue);

        await _client.PublishOrderEventAsync(new { eventId = Guid.NewGuid(), somethingElse = true },
            "OrderCancelledIntegrationEvent");

        await Task.Delay(TimeSpan.FromSeconds(2));

        Assert.Equal(before, await _client.QueueDepthAsync(BasketMessaging.InvalidQueue));
    }
}
