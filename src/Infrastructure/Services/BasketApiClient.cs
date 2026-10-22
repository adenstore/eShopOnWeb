using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Dtos;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class BasketApiClient : IBasketService, IBasketQueryService
{
    private readonly HttpClient _httpClient;

    public BasketApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<BasketDto> GetOrCreateBasketAsync(string buyerId)
    {
        // POST, not GET: the service creates the basket when the buyer has none yet.
        var response = await _httpClient.PostAsync($"api/baskets/{Uri.EscapeDataString(buyerId)}", content: null);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<BasketDto>())!;
    }

    public async Task<BasketDto?> GetBasketAsync(int basketId)
    {
        var response = await _httpClient.GetAsync($"api/baskets/{basketId}");
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<BasketDto>();
    }

    public async Task<BasketDto> AddItemToBasket(string username, int catalogItemId, decimal price, int quantity = 1)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"api/baskets/{Uri.EscapeDataString(username)}/items",
            new AddBasketItemRequest(catalogItemId, price, quantity));
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<BasketDto>())!;
    }

    public async Task<Result<BasketDto>> SetQuantities(int basketId, Dictionary<string, int> quantities)
    {
        var response = await _httpClient.PutAsJsonAsync(
            $"api/baskets/{basketId}/items", new SetBasketQuantitiesRequest(quantities));
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return Result<BasketDto>.NotFound();
        }
        response.EnsureSuccessStatusCode();

        return Result<BasketDto>.Success((await response.Content.ReadFromJsonAsync<BasketDto>())!);
    }

    public async Task DeleteBasketAsync(int basketId)
    {
        var response = await _httpClient.DeleteAsync($"api/baskets/{basketId}");
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
        var count = await _httpClient.GetFromJsonAsync<BasketItemCountResponse>(
            $"api/baskets/{Uri.EscapeDataString(username)}/items/count");

        return count?.Count ?? 0;
    }

    private record AddBasketItemRequest(int CatalogItemId, decimal UnitPrice, int Quantity);

    private record SetBasketQuantitiesRequest(Dictionary<string, int> Quantities);

    private record TransferBasketRequest(string AnonymousId, string UserName);

    private record BasketItemCountResponse(int Count);
}
