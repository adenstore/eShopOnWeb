using System.Net;
using System.Net.Http.Json;
using Microsoft.eShopWeb.OrderService.Contracts;
using Xunit;

namespace Microsoft.eShopWeb.OrderService.Tests.Endpoints;

public class OrderEndpointsTests : IClassFixture<OrderServiceApplication>
{
    private readonly HttpClient _client;

    public OrderEndpointsTests(OrderServiceApplication factory)
    {
        _client = factory.CreateClient();
    }

    private static CreateOrderRequest NewOrder(string buyerId) => new(
        buyerId,
        new AddressDto("123 Main St.", "Kent", "OH", "United States", "44240"),
        new List<CreateOrderItemDto>
        {
            new(1, ".NET Bot Black Sweatshirt", "http://catalogbaseurltobereplaced/images/products/1.png", 19.5m, 2),
            new(2, ".NET Black & White Mug", "http://catalogbaseurltobereplaced/images/products/2.png", 8.5m, 1)
        });

    [Fact]
    public async Task CreatesOrderAndReturnsItById()
    {
        var buyerId = Guid.NewGuid().ToString();

        var createResponse = await _client.PostAsJsonAsync("/api/orders", NewOrder(buyerId));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateOrderResponse>();

        var order = await _client.GetFromJsonAsync<OrderDto>($"/api/orders/{created!.OrderId}?buyerId={buyerId}");

        Assert.NotNull(order);
        Assert.Equal(buyerId, order.BuyerId);
        Assert.Equal(2, order.Items.Count);
        Assert.Equal(19.5m * 2 + 8.5m, order.Total);
        Assert.Equal("Kent", order.ShipToAddress.City);
    }

    [Fact]
    public async Task ListsOnlyTheBuyersOwnOrders()
    {
        var buyerId = Guid.NewGuid().ToString();
        await _client.PostAsJsonAsync("/api/orders", NewOrder(buyerId));
        await _client.PostAsJsonAsync("/api/orders", NewOrder(buyerId));
        await _client.PostAsJsonAsync("/api/orders", NewOrder(Guid.NewGuid().ToString()));

        var orders = await _client.GetFromJsonAsync<List<OrderDto>>($"/api/orders?buyerId={buyerId}");

        Assert.NotNull(orders);
        Assert.Equal(2, orders.Count);
        Assert.All(orders, o => Assert.Equal(buyerId, o.BuyerId));
    }

    [Fact]
    public async Task ReturnsNotFoundForAnotherBuyersOrder()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/orders", NewOrder(Guid.NewGuid().ToString()));
        var created = await createResponse.Content.ReadFromJsonAsync<CreateOrderResponse>();

        var response = await _client.GetAsync($"/api/orders/{created!.OrderId}?buyerId=someone-else");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RejectsOrderWithoutItems()
    {
        var request = NewOrder(Guid.NewGuid().ToString()) with { Items = new List<CreateOrderItemDto>() };

        var response = await _client.PostAsJsonAsync("/api/orders", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
