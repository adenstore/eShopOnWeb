namespace Microsoft.eShopWeb.BasketService.Contracts;

/// <summary>
/// Published to the <see cref="BasketMessaging.EventsExchange"/> fanout after every change to a
/// basket, so readers can keep a projection instead of querying this service to render a page.
/// <para>
/// Carries <b>full state, not a delta</b>. A fanout exchange gives neither global ordering nor
/// exactly-once delivery, so a delta stream ("+1 of item 7") would need both to project correctly.
/// Full state is idempotent — applying the same event twice is a no-op — and self-healing, since a
/// lost event is repaired by the next one for that basket.
/// </para>
/// <para>
/// A reader should apply an event only if <see cref="OccurredAt"/> is at or after what it already
/// holds for that buyer. Within one basket events are published in order, but a redelivery after a
/// broker or consumer restart can arrive behind a newer one, and without that check it would
/// resurrect stale items. Comparing timestamps is enough because a single basket is only ever
/// mutated by the single consumer on <see cref="BasketMessaging.CommandQueue"/>.
/// </para>
/// </summary>
public record BasketChangedIntegrationEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    BasketSnapshot Basket)
{
    public static BasketChangedIntegrationEvent For(BasketSnapshot basket) =>
        new(Guid.NewGuid(), DateTimeOffset.UtcNow, basket);
}
