using Microsoft.eShopWeb.BasketService.Contracts;
using Microsoft.eShopWeb.BasketService.Domain;

namespace Microsoft.eShopWeb.BasketService.Messaging;

/// <summary>
/// Turns a basket into the snapshot that goes on the wire.
/// <para>
/// This lives here rather than on <see cref="BasketSnapshot"/> itself because the contracts are a
/// separate assembly now, published for callers to reference. A factory taking
/// <see cref="Basket"/> would drag this service's EF entities along with it, and the gateway would
/// end up compiling against a DbContext to publish a message.
/// </para>
/// </summary>
public static class BasketSnapshotMapper
{
    public static BasketSnapshot ToSnapshot(this Basket basket) => new(
        basket.Id,
        basket.BuyerId,
        basket.Items.Select(ToSnapshot).ToList());

    private static BasketItemSnapshot ToSnapshot(BasketItem item) =>
        new(item.Id, item.CatalogItemId, item.UnitPrice, item.Quantity);
}
