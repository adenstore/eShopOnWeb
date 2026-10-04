namespace Microsoft.eShopWeb.OrderService.Contracts;

public record OrderDto(int Id, string BuyerId, DateTimeOffset OrderDate, AddressDto ShipToAddress, decimal Total, List<OrderItemDto> Items);

public record OrderItemDto(int CatalogItemId, string ProductName, string PictureUri, decimal UnitPrice, int Units);
