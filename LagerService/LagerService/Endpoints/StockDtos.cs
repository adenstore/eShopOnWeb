namespace Microsoft.eShopWeb.LagerService.Endpoints;

public record StockItemDto(int CatalogItemId, int Quantity);

public record SetQuantityRequest(int Quantity);

public record ChangeQuantityRequest(int Amount);
