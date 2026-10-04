namespace Microsoft.eShopWeb.OrderService.Contracts;

/// <summary>
/// Sent from the Web app to the Order microservice when a basket is checked out.
/// The item details are a snapshot taken at checkout time, so the Order service
/// never has to call back into the catalog.
/// </summary>
public record CreateOrderRequest(string BuyerId, AddressDto ShipToAddress, List<CreateOrderItemDto> Items);

public record CreateOrderItemDto(int CatalogItemId, string ProductName, string PictureUri, decimal UnitPrice, int Units);
