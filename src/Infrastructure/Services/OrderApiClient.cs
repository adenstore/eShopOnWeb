using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http.Json;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.OrderService.Contracts;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Talks to the Order microservice over HTTP. The HttpClient's BaseAddress is set
/// from the "OrderServiceBaseUrl" setting when it is registered.
/// </summary>
public class OrderApiClient : IOrderApiClient
{
    private readonly HttpClient _httpClient;

    public OrderApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<int> CreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("api/orders", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<CreateOrderResponse>(cancellationToken: cancellationToken);
        return created!.OrderId;
    }

    public async Task<IReadOnlyList<OrderDto>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _httpClient.GetFromJsonAsync<List<OrderDto>>(
            $"api/orders?buyerId={Uri.EscapeDataString(buyerId)}", cancellationToken);
        return orders ?? new List<OrderDto>();
    }

    public async Task<OrderDto?> GetOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(
            $"api/orders/{orderId}?buyerId={Uri.EscapeDataString(buyerId)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OrderDto>(cancellationToken: cancellationToken);
    }
}
