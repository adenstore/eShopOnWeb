using System.Net;
using System.Net.Http.Json;
using Microsoft.eShopWeb.LagerService.Endpoints;
using Xunit;

namespace Microsoft.eShopWeb.LagerService.Tests;

public class StockEndpointsTests : IClassFixture<LagerServiceApplication>
{
    private readonly HttpClient _client;

    public StockEndpointsTests(LagerServiceApplication factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetUnknownItemReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/stock/9999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SetThenGetReturnsQuantity()
    {
        await _client.PutAsJsonAsync("/api/stock/1", new SetQuantityRequest(20));

        var item = await _client.GetFromJsonAsync<StockItemDto>("/api/stock/1");

        Assert.Equal(new StockItemDto(1, 20), item);
    }

    [Fact]
    public async Task IncreaseAndDecreaseChangeQuantity()
    {
        await _client.PutAsJsonAsync("/api/stock/2", new SetQuantityRequest(5));

        await _client.PostAsJsonAsync("/api/stock/2/increase", new ChangeQuantityRequest(10));
        var response = await _client.PostAsJsonAsync("/api/stock/2/decrease", new ChangeQuantityRequest(3));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new StockItemDto(2, 12), await response.Content.ReadFromJsonAsync<StockItemDto>());
    }

    [Fact]
    public async Task DecreaseMoreThanInStockReturnsConflictAndKeepsQuantity()
    {
        await _client.PutAsJsonAsync("/api/stock/3", new SetQuantityRequest(2));

        var response = await _client.PostAsJsonAsync("/api/stock/3/decrease", new ChangeQuantityRequest(5));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(new StockItemDto(3, 2), await _client.GetFromJsonAsync<StockItemDto>("/api/stock/3"));
    }

    [Fact]
    public async Task NegativeQuantityIsRejected()
    {
        var response = await _client.PutAsJsonAsync("/api/stock/4", new SetQuantityRequest(-1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
