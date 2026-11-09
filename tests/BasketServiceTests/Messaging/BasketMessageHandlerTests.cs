using Ardalis.Result;
using Microsoft.eShopWeb.BasketService.Contracts;
using Microsoft.eShopWeb.BasketService.Domain;
using Microsoft.eShopWeb.BasketService.Interfaces;
using Microsoft.eShopWeb.BasketService.Messaging;
using Microsoft.eShopWeb.BasketServiceTests.Builders;
using Microsoft.eShopWeb.OrderService.Contracts;
using Microsoft.eShopWeb.OrderService.Contracts.Events;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.BasketServiceTests.Messaging;

/// <summary>
/// The handler is where every message rule lives, with no AMQP in it, so all of this runs without
/// a broker.
/// </summary>
public class BasketMessageHandlerTests
{
    private readonly IBasketService _basketService = Substitute.For<IBasketService>();
    private readonly IBasketEventPublisher _events = Substitute.For<IBasketEventPublisher>();
    private readonly BasketMessageHandler _handler;

    public BasketMessageHandlerTests()
    {
        _handler = new BasketMessageHandler(_basketService, _events, NullLogger<BasketMessageHandler>.Instance);
    }

    private static Basket BasketFor(string buyerId, int id = 1)
    {
        var basket = Substitute.For<Basket>(buyerId);
        basket.Id.Returns(id);
        return basket;
    }

    [Fact]
    public async Task AddItemPassesTheCommandThroughAndAnnouncesTheResult()
    {
        var builder = new BasketBuilder();
        var basket = builder.WithNoItems();
        _basketService.AddItemToBasket(builder.BasketBuyerId, 7, 9.99m, 3).Returns(basket);

        await _handler.HandleAsync(new AddItemToBasketCommand
        {
            BuyerId = builder.BasketBuyerId,
            CatalogItemId = 7,
            UnitPrice = 9.99m,
            Quantity = 3
        });

        await _basketService.Received(1).AddItemToBasket(builder.BasketBuyerId, 7, 9.99m, 3);
        await _events.Received(1).PublishBasketChangedAsync(
            Arg.Is<BasketSnapshot>(s => s.BuyerId == builder.BasketBuyerId && s.BasketId == builder.BasketId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetQuantitiesAnnouncesTheUpdatedBasket()
    {
        var basket = BasketFor("buyer@example.com", id: 4);
        basket.AddItem(2, 3.40m, 4);
        _basketService.SetQuantities(4, Arg.Any<Dictionary<string, int>>()).Returns(Result<Basket>.Success(basket));

        await _handler.HandleAsync(new SetBasketQuantitiesCommand
        {
            BasketId = 4,
            Quantities = new Dictionary<string, int> { ["1"] = 2 }
        });

        await _events.Received(1).PublishBasketChangedAsync(
            Arg.Is<BasketSnapshot>(s => s.BasketId == 4 && s.TotalItems == 4),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetQuantitiesOnAMissingBasketIsSwallowedRatherThanThrown()
    {
        // The basket is most likely already checked out. Throwing would dead-letter a message that
        // can never succeed, so the handler logs and returns — and must not announce a change.
        _basketService.SetQuantities(99, Arg.Any<Dictionary<string, int>>()).Returns(Result<Basket>.NotFound());

        await _handler.HandleAsync(new SetBasketQuantitiesCommand { BasketId = 99 });

        await _events.DidNotReceive().PublishBasketChangedAsync(Arg.Any<BasketSnapshot>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TransferAnnouncesBothTheUserBasketAndTheEmptiedAnonymousOne()
    {
        var anonymous = BasketFor("cookie-id", id: 10);
        var user = BasketFor("buyer@example.com", id: 11);
        user.AddItem(2, 3.40m, 4);
        _basketService.GetBasketByBuyerAsync("cookie-id").Returns(anonymous);
        _basketService.GetBasketByBuyerAsync("buyer@example.com").Returns(user);

        await _handler.HandleAsync(new TransferBasketCommand
        {
            AnonymousId = "cookie-id",
            UserName = "buyer@example.com"
        });

        await _basketService.Received(1).TransferBasketAsync("cookie-id", "buyer@example.com");

        // Without the second event a reader keyed by buyer keeps showing the anonymous basket's
        // items under the cookie id forever.
        await _events.Received(1).PublishBasketChangedAsync(
            Arg.Is<BasketSnapshot>(s => s.BuyerId == "buyer@example.com" && s.TotalItems == 4),
            Arg.Any<CancellationToken>());
        await _events.Received(1).PublishBasketChangedAsync(
            Arg.Is<BasketSnapshot>(s => s.BuyerId == "cookie-id" && s.BasketId == 10 && s.Items.Count == 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TransferWithNothingToMoveDoesNothing()
    {
        _basketService.GetBasketByBuyerAsync("cookie-id").Returns((Basket?)null);

        await _handler.HandleAsync(new TransferBasketCommand
        {
            AnonymousId = "cookie-id",
            UserName = "buyer@example.com"
        });

        await _basketService.DidNotReceive().TransferBasketAsync(Arg.Any<string>(), Arg.Any<string>());
        await _events.DidNotReceive().PublishBasketChangedAsync(Arg.Any<BasketSnapshot>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OrderCreatedClearsTheBuyersBasketAndAnnouncesItEmpty()
    {
        var basket = BasketFor("buyer@example.com", id: 42);
        basket.AddItem(2, 3.40m, 4);
        _basketService.GetBasketByBuyerAsync("buyer@example.com").Returns(basket);

        await _handler.HandleAsync(OrderCreated("buyer@example.com"));

        await _basketService.Received(1).DeleteBasketAsync(42);
        await _events.Received(1).PublishBasketChangedAsync(
            Arg.Is<BasketSnapshot>(s => s.BasketId == 42 && s.BuyerId == "buyer@example.com" && s.Items.Count == 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OrderCreatedIsANoOpWhenTheBasketIsAlreadyGone()
    {
        // This is the redelivery case, and the reason clearing the basket on an event is safe where
        // the old three-call checkout was not.
        _basketService.GetBasketByBuyerAsync("buyer@example.com").Returns((Basket?)null);

        await _handler.HandleAsync(OrderCreated("buyer@example.com"));

        await _basketService.DidNotReceive().DeleteBasketAsync(Arg.Any<int>());
        await _events.DidNotReceive().PublishBasketChangedAsync(Arg.Any<BasketSnapshot>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QueryByBuyerReturnsTheBasketWithItsItemIds()
    {
        var basket = BasketFor("buyer@example.com", id: 5);
        basket.AddItem(2, 3.40m, 4);
        _basketService.GetBasketByBuyerAsync("buyer@example.com").Returns(basket);

        var reply = await _handler.HandleAsync(new GetBasketByBuyerRequest { BuyerId = "buyer@example.com" });

        Assert.Null(reply.Error);
        Assert.NotNull(reply.Basket);
        Assert.Equal(5, reply.Basket!.BasketId);
        Assert.Equal(4, reply.Basket.TotalItems);
        Assert.Equal(2, reply.Basket.Items.Single().CatalogItemId);
    }

    [Fact]
    public async Task QueryForABuyerWithNoBasketIsNotAnError()
    {
        // Baskets are created lazily on the first add, so "no basket" is a normal answer and must
        // be distinguishable from a failure the caller should retry.
        _basketService.GetBasketByBuyerAsync("stranger").Returns((Basket?)null);

        var reply = await _handler.HandleAsync(new GetBasketByBuyerRequest { BuyerId = "stranger" });

        Assert.Null(reply.Basket);
        Assert.Null(reply.Error);
    }

    [Fact]
    public async Task QueryByIdDoesNotCreateABasket()
    {
        _basketService.GetBasketAsync(77).Returns((Basket?)null);

        var reply = await _handler.HandleAsync(new GetBasketByIdRequest { BasketId = 77 });

        Assert.Null(reply.Basket);
        await _basketService.DidNotReceive().GetOrCreateBasketAsync(Arg.Any<string>());
    }

    private static OrderCreatedIntegrationEvent OrderCreated(string buyerId) => new(
        Guid.NewGuid(),
        DateTimeOffset.UtcNow,
        OrderId: 1,
        BuyerId: buyerId,
        Total: 13.60m,
        Items: new List<OrderItemDto> { new(2, "Product", "picture.png", 3.40m, 4) });
}
