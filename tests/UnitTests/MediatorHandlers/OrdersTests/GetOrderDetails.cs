using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.OrderService.Contracts;
using Microsoft.eShopWeb.Web.Features.OrderDetails;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.MediatorHandlers.OrdersTests;

public class GetOrderDetails
{
    private readonly IOrderApiClient _mockOrderApiClient = Substitute.For<IOrderApiClient>();

    public GetOrderDetails()
    {
        var item = new OrderItemDto(1, "ProductName", "URI", 10.00m, 10);
        var address = new AddressDto("", "", "", "", "");
        var order = new OrderDto(0, "SomeUserName", System.DateTimeOffset.Now, address, 100m, new List<OrderItemDto> { item });

        _mockOrderApiClient.GetOrderAsync("SomeUserName", 0, Arg.Any<CancellationToken>())
            .Returns(order);
    }

    [Fact]
    public async Task NotBeNullIfOrderExists()
    {
        var request = new eShopWeb.Web.Features.OrderDetails.GetOrderDetails("SomeUserName", 0);

        var handler = new GetOrderDetailsHandler(_mockOrderApiClient);

        var result = await handler.Handle(request, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Single(result.OrderItems);
    }

    [Fact]
    public async Task IsNullIfOrderDoesNotExist()
    {
        var request = new eShopWeb.Web.Features.OrderDetails.GetOrderDetails("SomeUserName", 42);

        var handler = new GetOrderDetailsHandler(_mockOrderApiClient);

        var result = await handler.Handle(request, CancellationToken.None);

        Assert.Null(result);
    }
}
