namespace Microsoft.eShopWeb.ApiGateway.Endpoints;

/// <summary>
/// The one read response shape the gateway defines itself. Everything else it answers with is
/// <see cref="BasketService.Contracts.BasketSnapshot"/> — the published contract, unchanged, rather
/// than a DTO of its own: the gateway is a transport boundary here, and re-describing the basket
/// would be a second wire format to keep in step for no gain.
/// </summary>
/// <remarks>
/// The count has its own route because it is the hottest basket read in the shop — a ViewComponent
/// in the shared layout renders it on every page — and shipping a whole basket to display one number
/// would waste most of the response. Same JSON shape the deleted basket REST API used.
/// </remarks>
public record BasketItemCountResponse(int Count);
