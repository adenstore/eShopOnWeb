using Microsoft.eShopWeb.OrderService.Contracts;
using Microsoft.eShopWeb.OrderService.Domain;

namespace Microsoft.eShopWeb.OrderService.Endpoints;

public static class OrderMappings
{
    public static OrderDto ToDto(this Order order) => new(
        order.Id,
        order.BuyerId,
        order.OrderDate,
        new AddressDto(order.ShipToAddress.Street, order.ShipToAddress.City, order.ShipToAddress.State,
            order.ShipToAddress.Country, order.ShipToAddress.ZipCode),
        order.Total(),
        order.OrderItems.Select(ToDto).ToList());

    public static OrderItemDto ToDto(this OrderItem item) => new(
        item.ItemOrdered.CatalogItemId,
        item.ItemOrdered.ProductName,
        item.ItemOrdered.PictureUri,
        item.UnitPrice,
        item.Units);

    public static Order ToOrder(this CreateOrderRequest request)
    {
        var address = new Address(request.ShipToAddress.Street, request.ShipToAddress.City,
            request.ShipToAddress.State, request.ShipToAddress.Country, request.ShipToAddress.ZipCode);

        var items = request.Items
            .Select(i => new OrderItem(new CatalogItemOrdered(i.CatalogItemId, i.ProductName, i.PictureUri), i.UnitPrice, i.Units))
            .ToList();

        return new Order(request.BuyerId, address, items);
    }
}
