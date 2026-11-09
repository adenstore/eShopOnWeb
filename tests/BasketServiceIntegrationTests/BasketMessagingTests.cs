using Microsoft.eShopWeb.BasketService.Contracts;
using Xunit;

namespace Microsoft.eShopWeb.BasketServiceIntegrationTests;

/// <summary>
/// BasketService against a real RabbitMQ in a container. These cover the ground the unit tests
/// cannot: that messages route, that rejected ones dead-letter, and that request/reply works over
/// the broker rather than over an in-process method call.
/// </summary>
[Collection(BasketServiceCollection.Name)]
public class BasketMessagingTests : IAsyncLifetime
{
    private readonly BasketServiceApplication _app;
    private BasketMessageClient _client = null!;

    // A buyer per test, so one test's basket cannot satisfy another's assertion.
    private readonly string _buyer = $"buyer-{Guid.NewGuid()}@test.com";

    public BasketMessagingTests(BasketServiceApplication app) => _app = app;

    public async Task InitializeAsync() => _client = await BasketMessageClient.ConnectAsync(_app);

    public async Task DisposeAsync() => await _client.DisposeAsync();

    [Fact]
    public async Task ServiceIsHealthyWithItsConsumersAttached()
    {
        using var http = _app.CreateClient();

        var response = await http.GetAsync("/health");

        response.EnsureSuccessStatusCode();
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AddingAnItemCreatesTheBasketAndAnnouncesIt()
    {
        await _client.SendAsync(BasketMessaging.RoutingKeys.AddItem,
            new { buyerId = _buyer, catalogItemId = 2, unitPrice = 3.40m, quantity = 4 });

        var changed = await PolledWait.ForValueAsync(() =>
            Task.FromResult(_client.LatestEventFor(_buyer)));

        Assert.True(changed is not null, "no BasketChanged arrived on basket-events");
        Assert.Equal(4, changed!.Basket.TotalItems);
        Assert.Equal(2, changed.Basket.Items.Single().CatalogItemId);

        // Full state, not a delta: a reader can apply this without knowing what came before.
        Assert.Equal(_buyer, changed.Basket.BuyerId);
        Assert.True(changed.Basket.BasketId > 0);
    }

    [Fact]
    public async Task AQueryPublishedRightAfterACommandSeesThatCommand()
    {
        // Read-your-own-writes, with no delay and no PolledWait on purpose. Commands and queries
        // share one FIFO queue with a single consumer at prefetch 1, so the query is handled after
        // the command. If that ever stops being true this test fails rather than going flaky.
        await _client.SendAsync(BasketMessaging.RoutingKeys.AddItem,
            new { buyerId = _buyer, catalogItemId = 2, unitPrice = 3.40m, quantity = 4 });

        var reply = await _client.GetBasketByBuyerAsync(_buyer);

        Assert.True(reply is not null, "the query never got a reply");
        Assert.Null(reply!.Error);
        Assert.NotNull(reply.Basket);
        Assert.Equal(4, reply.Basket!.TotalItems);
    }

    [Fact]
    public async Task AskingForABasketThatDoesNotExistIsAnAnswerNotAnError()
    {
        // Baskets are created lazily on the first add, so "no basket" is normal and has to be
        // distinguishable from a failure worth retrying.
        var reply = await _client.GetBasketByBuyerAsync($"nobody-{Guid.NewGuid()}");

        Assert.True(reply is not null, "the query never got a reply");
        Assert.Null(reply!.Basket);
        Assert.Null(reply.Error);
    }

    [Fact]
    public async Task TheCheckoutQueryFindsTheBasketByItsOwnId()
    {
        await _client.SendAsync(BasketMessaging.RoutingKeys.AddItem,
            new { buyerId = _buyer, catalogItemId = 2, unitPrice = 3.40m, quantity = 1 });
        var byBuyer = await _client.GetBasketByBuyerAsync(_buyer);
        var basketId = byBuyer!.Basket!.BasketId;

        var byId = await _client.GetBasketByIdAsync(basketId);

        Assert.Equal(basketId, byId!.Basket!.BasketId);
        Assert.Equal(_buyer, byId.Basket.BuyerId);
    }

    [Fact]
    public async Task SettingAQuantityToZeroRemovesTheLine()
    {
        await _client.SendAsync(BasketMessaging.RoutingKeys.AddItem,
            new { buyerId = _buyer, catalogItemId = 2, unitPrice = 3.40m, quantity = 4 });
        var basket = (await _client.GetBasketByBuyerAsync(_buyer))!.Basket!;
        var itemId = basket.Items.Single().Id;

        await _client.SendAsync(BasketMessaging.RoutingKeys.SetQuantities,
            new { basketId = basket.BasketId, quantities = new Dictionary<string, int> { [itemId.ToString()] = 0 } });

        var emptied = await PolledWait.UntilAsync(async () =>
        {
            var reply = await _client.GetBasketByBuyerAsync(_buyer);
            return reply?.Basket is not null && reply.Basket.Items.Count == 0;
        });

        Assert.True(emptied, "the zero-quantity line was never removed");
    }

    [Fact]
    public async Task TransferMovesAnAnonymousBasketOntoTheUsername()
    {
        var cookieId = $"cookie-{Guid.NewGuid()}";
        await _client.SendAsync(BasketMessaging.RoutingKeys.AddItem,
            new { buyerId = cookieId, catalogItemId = 5, unitPrice = 1.25m, quantity = 3 });
        await PolledWait.ForValueAsync(() => Task.FromResult(_client.LatestEventFor(cookieId)));

        await _client.SendAsync(BasketMessaging.RoutingKeys.Transfer,
            new { anonymousId = cookieId, userName = _buyer });

        var moved = await PolledWait.UntilAsync(async () =>
        {
            var reply = await _client.GetBasketByBuyerAsync(_buyer);
            return reply?.Basket?.TotalItems == 3;
        });
        Assert.True(moved, "the user's basket never absorbed the anonymous items");

        var anonymousGone = await PolledWait.UntilAsync(async () =>
            (await _client.GetBasketByBuyerAsync(cookieId))?.Basket is null);
        Assert.True(anonymousGone, "the anonymous basket still exists");

        // The emptied event matters as much as the move: without it a projection keyed by buyer
        // keeps showing the anonymous basket's items under the cookie id forever.
        var emptyAnnounced = await PolledWait.UntilAsync(() =>
            Task.FromResult(_client.LatestEventFor(cookieId) is { Basket.Items.Count: 0 }));
        Assert.True(emptyAnnounced, "no empty BasketChanged went out for the cookie id");
    }

    [Fact]
    public async Task AnInvalidCommandIsDeadLetteredAndNotApplied()
    {
        var before = await _client.QueueDepthAsync(BasketMessaging.InvalidQueue);

        // Quantity 0 is not an add. Nothing invokes DataAnnotations for a queue consumer, so this
        // only fails if MessageValidator is actually running.
        await _client.SendAsync(BasketMessaging.RoutingKeys.AddItem,
            new { buyerId = _buyer, catalogItemId = 2, unitPrice = 3.40m, quantity = 0 });

        var deadLettered = await PolledWait.UntilAsync(async () =>
            await _client.QueueDepthAsync(BasketMessaging.InvalidQueue) > before);
        Assert.True(deadLettered, "the invalid command never reached the invalid queue");

        var reply = await _client.GetBasketByBuyerAsync(_buyer);
        Assert.Null(reply!.Basket);
    }

    [Fact]
    public async Task MalformedJsonIsDeadLetteredRatherThanCrashingTheConsumer()
    {
        var before = await _client.QueueDepthAsync(BasketMessaging.InvalidQueue);

        await _client.SendRawAsync(BasketMessaging.RoutingKeys.AddItem, "{ this is not json"u8.ToArray());

        var deadLettered = await PolledWait.UntilAsync(async () =>
            await _client.QueueDepthAsync(BasketMessaging.InvalidQueue) > before);
        Assert.True(deadLettered, "malformed JSON never reached the invalid queue");

        // The consumer has to still be working afterwards; a poison message must not stop the queue.
        await _client.SendAsync(BasketMessaging.RoutingKeys.AddItem,
            new { buyerId = _buyer, catalogItemId = 2, unitPrice = 3.40m, quantity = 1 });
        var stillWorking = await PolledWait.UntilAsync(async () =>
            (await _client.GetBasketByBuyerAsync(_buyer))?.Basket?.TotalItems == 1);
        Assert.True(stillWorking, "the consumer stopped working after a poison message");
    }

    [Fact]
    public async Task AMessageNobodyBoundIsCaughtInsteadOfVanishing()
    {
        var before = await _client.QueueDepthAsync(BasketMessaging.UnroutableQueue);

        await _client.SendAsync("Basket.somethingNobodyBound", new { buyerId = _buyer });

        var caught = await PolledWait.UntilAsync(async () =>
            await _client.QueueDepthAsync(BasketMessaging.UnroutableQueue) > before);

        Assert.True(caught, "the alternate exchange did not catch the unroutable message");
    }
}
