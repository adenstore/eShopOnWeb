using Microsoft.eShopWeb.LagerService.Data;
using Microsoft.eShopWeb.LagerService.Messaging;
using Microsoft.eShopWeb.OrderService.Contracts;
using Microsoft.eShopWeb.OrderService.Contracts.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.LagerService.Tests;

public class OrderCreatedHandlerTests
{
    private readonly InMemoryStockRepository _stock = new();
    private readonly OrderCreatedHandler _handler;

    public OrderCreatedHandlerTests()
    {
        _handler = new OrderCreatedHandler(_stock, NullLogger<OrderCreatedHandler>.Instance);
    }

    private static OrderCreatedIntegrationEvent CreateEvent(params (int CatalogItemId, int Units)[] items) =>
        new(Guid.NewGuid(), DateTimeOffset.UtcNow, OrderId: 1, BuyerId: "buyer", Total: 0,
            items.Select(i => new OrderItemDto(i.CatalogItemId, "Product", "", 1m, i.Units)).ToList());

    [Fact]
    public async Task OrderTakesItemsOutOfStock()
    {
        await _stock.SetQuantityAsync(1, 10);
        await _stock.SetQuantityAsync(2, 5);

        await _handler.HandleAsync(CreateEvent((1, 3), (2, 5)));

        Assert.Equal(7, await _stock.GetQuantityAsync(1));
        Assert.Equal(0, await _stock.GetQuantityAsync(2));
    }

    [Fact]
    public async Task SameEventTwiceIsOnlyCountedOnce()
    {
        await _stock.SetQuantityAsync(1, 10);
        var orderCreated = CreateEvent((1, 3));

        await _handler.HandleAsync(orderCreated);
        await _handler.HandleAsync(orderCreated);

        Assert.Equal(7, await _stock.GetQuantityAsync(1));
    }

    [Fact]
    public async Task NotEnoughInStockLeavesQuantityUnchanged()
    {
        await _stock.SetQuantityAsync(1, 2);

        await _handler.HandleAsync(CreateEvent((1, 5)));

        Assert.Equal(2, await _stock.GetQuantityAsync(1));
    }
}
