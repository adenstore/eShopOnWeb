using System.Collections.Concurrent;
using System.Threading;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.OrderService.Contracts;

namespace Microsoft.eShopWeb.FunctionalTests.Web;

/// <summary>
/// Stands in for the Order microservice so the Web app's functional tests
/// don't need the service running.
/// </summary>
public class FakeOrderApiClient : IOrderApiClient
{
    private readonly ConcurrentDictionary<int, OrderDto> _orders = new();
    private int _nextId;

    public Task<int> CreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken = default)
    {
        var id = Interlocked.Increment(ref _nextId);
        var items = request.Items
            .Select(i => new OrderItemDto(i.CatalogItemId, i.ProductName, i.PictureUri, i.UnitPrice, i.Units))
            .ToList();
        _orders[id] = new OrderDto(id, request.BuyerId, DateTimeOffset.Now, request.ShipToAddress,
            items.Sum(i => i.UnitPrice * i.Units), items);
        return Task.FromResult(id);
    }

    public Task<IReadOnlyList<OrderDto>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<OrderDto> orders = _orders.Values.Where(o => o.BuyerId == buyerId).ToList();
        return Task.FromResult(orders);
    }

    public Task<OrderDto?> GetOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_orders.TryGetValue(orderId, out var order) && order.BuyerId == buyerId ? order : null);
    }
}
