using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Web.ViewModels;

namespace Microsoft.eShopWeb.Web.Features.OrderDetails;

public class GetOrderDetailsHandler : IRequestHandler<GetOrderDetails, OrderDetailViewModel?>
{
    private readonly IOrderApiClient _orderApiClient;

    public GetOrderDetailsHandler(IOrderApiClient orderApiClient)
    {
        _orderApiClient = orderApiClient;
    }

    public async Task<OrderDetailViewModel?> Handle(GetOrderDetails request,
        CancellationToken cancellationToken)
    {
        var order = await _orderApiClient.GetOrderAsync(request.UserName, request.OrderId, cancellationToken);

        if (order == null)
        {
            return null;
        }

        return new OrderDetailViewModel
        {
            OrderDate = order.OrderDate,
            OrderItems = order.Items.Select(oi => new OrderItemViewModel
            {
                PictureUrl = oi.PictureUri,
                ProductId = oi.CatalogItemId,
                ProductName = oi.ProductName,
                UnitPrice = oi.UnitPrice,
                Units = oi.Units
            }).ToList(),
            OrderNumber = order.Id,
            ShippingAddress = order.ShipToAddress,
            Total = order.Total
        };
    }
}
