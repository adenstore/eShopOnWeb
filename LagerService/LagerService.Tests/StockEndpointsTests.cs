using FluentAssertions;
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

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SetThenGetReturnsQuantity()
    {
        await _client.PutAsJsonAsync("/api/stock/1", new SetQuantityRequest(20));

        var item = await _client.GetFromJsonAsync<StockItemDto>("/api/stock/1");

        item.Should().Be(new StockItemDto(1, 20));
    }

    [Fact]
    public async Task IncreaseAndDecreaseChangeQuantity()
    {
        await _client.PutAsJsonAsync("/api/stock/2", new SetQuantityRequest(5));

        await _client.PostAsJsonAsync("/api/stock/2/increase", new ChangeQuantityRequest(10));
        var response = await _client.PostAsJsonAsync("/api/stock/2/decrease", new ChangeQuantityRequest(3));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<StockItemDto>()).Should().Be(new StockItemDto(2, 12));
    }

    [Fact]
    public async Task DecreaseMoreThanInStockReturnsConflictAndKeepsQuantity()
    {
        await _client.PutAsJsonAsync("/api/stock/3", new SetQuantityRequest(2));

        var response = await _client.PostAsJsonAsync("/api/stock/3/decrease", new ChangeQuantityRequest(5));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _client.GetFromJsonAsync<StockItemDto>("/api/stock/3")).Should().Be(new StockItemDto(3, 2));
    }

    [Fact]
    public async Task NegativeQuantityIsRejected()
    {
        var response = await _client.PutAsJsonAsync("/api/stock/4", new SetQuantityRequest(-1));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
