namespace Microsoft.eShopWeb.ApiGateway.Endpoints;

/// <summary>
/// The REST bodies the frontend sends. They are not the same types as the commands in
/// BasketService.Contracts on purpose: the buyer and the basket id are in the URL, where a REST
/// resource keeps its identity, and only what is left over travels in the body. The endpoint
/// combines the two into the command it publishes.
/// </summary>
/// <remarks>
/// Same JSON shapes the deleted basket REST API used, so the client calling the gateway keeps
/// working unchanged — only the status codes it gets back change.
/// </remarks>
public record AddBasketItemRequest(int CatalogItemId, decimal UnitPrice, int Quantity = 1);

public record SetBasketQuantitiesRequest(Dictionary<string, int> Quantities);

public record TransferBasketRequest(string AnonymousId, string UserName);
