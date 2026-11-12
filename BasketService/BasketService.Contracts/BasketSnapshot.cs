namespace Microsoft.eShopWeb.BasketService.Contracts;

/// <summary>
/// A whole basket as it crosses the service boundary — the payload of both
/// <see cref="BasketChangedIntegrationEvent"/> and the query replies.
/// <para>
/// Carries no catalog data beyond <see cref="BasketItemSnapshot.CatalogItemId"/>, which is what
/// lets this service keep a database of its own. Denormalising product name and picture onto the
/// snapshot would let the Web layer stop reading catalog tables for basket rendering, but it would
/// also mean BasketService holding a catalog projection fed by "catalog-events" — including a
/// decision about what happens to an item already sitting in someone's basket when the catalog
/// deletes it. That trade is not made here; adding the fields later is additive, not breaking.
/// </para>
/// </summary>
public record BasketSnapshot(int BasketId, string BuyerId, IReadOnlyList<BasketItemSnapshot> Items)
{
    /// <summary>
    /// The header item count, so a reader serving it from a projection does not need a second
    /// round-trip. Computed, not stored — it is always consistent with <see cref="Items"/>.
    /// </summary>
    public int TotalItems => Items.Sum(i => i.Quantity);

    /// <summary>
    /// The state of a basket that no longer exists. Published after checkout clears one: readers
    /// apply full state unconditionally, so an empty snapshot needs no separate "deleted" event
    /// and no special case on the projection side.
    /// </summary>
    public static BasketSnapshot Empty(int basketId, string buyerId) =>
        new(basketId, buyerId, Array.Empty<BasketItemSnapshot>());
}

/// <summary>
/// <see cref="Id"/> is the basket *item* id and is load-bearing: it is the key
/// <see cref="SetBasketQuantitiesCommand.Quantities"/> is addressed by, so a reader that renders a
/// basket for editing has to round-trip it.
/// </summary>
public record BasketItemSnapshot(int Id, int CatalogItemId, decimal UnitPrice, int Quantity);
