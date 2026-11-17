using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.BasketService.Contracts;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Talks to the basket through the ApiGateway. The Web app is the frontend, so it keeps speaking
/// REST; the gateway turns these calls into RabbitMQ messages, which is the only interface
/// BasketService has.
/// <para>
/// The writes answer <c>202 Accepted</c> — the broker has the command, the basket has not changed
/// yet — so they return nothing. The one read that matters for money,
/// <see cref="GetBasketAsync(int)"/>, is answered from BasketService itself rather than from the
/// gateway's projection; see <c>src/ApiGateway/README.md</c>.
/// </para>
/// </summary>
public class BasketApiClient : IBasketService, IBasketQueryService
{
    private readonly HttpClient _httpClient;

    public BasketApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<BasketSnapshot> GetBasketAsync(string buyerId)
    {
        // GET, not POST: nothing is created by reading. A buyer with no basket gets an empty one
        // at IBasketService.NoBasketId rather than a 404, because for a first-time visitor that is
        // the normal case and not an error.
        var response = await _httpClient.GetAsync($"api/baskets/{Uri.EscapeDataString(buyerId)}");
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<BasketSnapshot>())!;
    }

    public async Task<BasketSnapshot?> GetBasketAsync(int basketId)
    {
        var response = await _httpClient.GetAsync($"api/baskets/{basketId}");
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<BasketSnapshot>();
    }

    public async Task AddItemToBasket(string username, int catalogItemId, decimal price, int quantity = 1)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"api/baskets/{Uri.EscapeDataString(username)}/items",
            new AddBasketItemRequest(catalogItemId, price, quantity));
        response.EnsureSuccessStatusCode();
    }

    public async Task SetQuantities(int basketId, Dictionary<string, int> quantities)
    {
        var response = await _httpClient.PutAsJsonAsync(
            $"api/baskets/{basketId}/items", new SetBasketQuantitiesRequest(quantities));
        response.EnsureSuccessStatusCode();
    }

    public async Task TransferBasketAsync(string anonymousId, string userName)
    {
        var response = await _httpClient.PostAsJsonAsync(
            "api/baskets/transfer", new TransferBasketRequest(anonymousId, userName));
        response.EnsureSuccessStatusCode();
    }

    public async Task<int> CountTotalBasketItems(string username)
    {
        // The hottest basket call in the shop — a ViewComponent in the shared layout renders it on
        // every page — which is why the gateway serves it from a projection instead of a broker
        // round-trip, and why it has its own route rather than counting a whole basket here.
        var count = await _httpClient.GetFromJsonAsync<BasketItemCountResponse>(
            $"api/baskets/{Uri.EscapeDataString(username)}/items/count");

        return count?.Count ?? 0;
    }

    // The gateway's REST bodies: the buyer and the basket id travel in the URL, so only what is
    // left over goes in the body. Deliberately not the command types from BasketService.Contracts,
    // which is where the gateway puts them back together.
    private record AddBasketItemRequest(int CatalogItemId, decimal UnitPrice, int Quantity);

    private record SetBasketQuantitiesRequest(Dictionary<string, int> Quantities);

    private record TransferBasketRequest(string AnonymousId, string UserName);

    private record BasketItemCountResponse(int Count);
}
