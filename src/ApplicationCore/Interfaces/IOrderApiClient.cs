using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.OrderService.Contracts;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The monolith's view of the Order microservice. Implemented over HTTP in Infrastructure.
/// </summary>
public interface IOrderApiClient
{
    Task<int> CreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrderDto>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);
    Task<OrderDto?> GetOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);
}
