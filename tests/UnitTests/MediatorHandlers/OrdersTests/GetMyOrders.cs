using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.OrderService.Contracts;
using Microsoft.eShopWeb.Web.Features.MyOrders;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.MediatorHandlers.OrdersTests;

public class GetMyOrders
{
    private readonly IOrderApiClient _mockOrderApiClient = Substitute.For<IOrderApiClient>();

    public GetMyOrders()
    {
        var item = new OrderItemDto(1, "ProductName", "URI", 10.00m, 10);
        var address = new AddressDto("", "", "", "", "");
        var order = new OrderDto(1, "buyerId", System.DateTimeOffset.Now, address, 100m, new List<OrderItemDto> { item });

        _mockOrderApiClient.GetOrdersForBuyerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<OrderDto> { order });
    }

    [Fact]
    public async Task NotReturnNullIfOrdersArePresIent()
    {
        var request = new eShopWeb.Web.Features.MyOrders.GetMyOrders("SomeUserName");

        var handler = new GetMyOrdersHandler(_mockOrderApiClient);

        var result = await handler.Handle(request, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Single(result);
    }
}
