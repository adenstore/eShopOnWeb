using Microsoft.eShopWeb.BasketService.Contracts;

namespace Microsoft.eShopWeb.ApiGateway.Projections;

/// <summary>
/// The gateway's read model for baskets, kept up to date from the "basket-events" fanout. It exists
/// so that reading a basket does not cost a broker round-trip: the header item count renders on
/// every single page of the shop, and request/reply there would put a queue hop on every page view.
/// <para>
/// The projection is an accelerator, never the authority. Every read it cannot answer confidently
/// falls through to a query against BasketService, which is correct by construction — commands and
/// queries share one FIFO queue with a single consumer, so a query published after a command is
/// always handled after it.
/// </para>
/// </summary>
public interface IBasketProjection
{
    /// <summary>
    /// What the projection can say about a buyer right now. Answering
    /// <see cref="ProjectedBasketKind.Unknown"/> is normal, not a failure: it is how the projection
    /// declines to answer for a buyer whose own write has not landed yet.
    /// </summary>
    Task<ProjectedBasket> ReadAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The buyer a basket id belongs to, or null if the projection has never seen that basket. Only
    /// the basket id reaches the gateway on <c>PUT /api/baskets/{basketId}/items</c>, and
    /// <see cref="NoteWriteInFlightAsync"/> is keyed by buyer, so the write path needs this to know
    /// whose reads to stop trusting.
    /// </summary>
    Task<string?> OwnerOfAsync(int basketId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a <see cref="BasketChangedIntegrationEvent"/>, keeping whichever state is newer.
    /// Events carry full baskets rather than deltas, so applying one twice is a no-op and a lost one
    /// is repaired by the next change to that basket.
    /// </summary>
    Task ApplyAsync(BasketChangedIntegrationEvent changed, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that a buyer has no basket, after BasketService has said so. Without this, every
    /// page view by an anonymous shopper who has not added anything yet — the commonest request in
    /// the shop — would miss the projection and fall through to a query, which is exactly the
    /// round-trip the projection exists to avoid.
    /// <para>
    /// Only the *absence* of a basket is cached from a query answer, never a basket itself: a query
    /// reply carries no event timestamp, so there would be nothing to order it against the events
    /// arriving on the fanout, and the event that does carry one is already on its way. An absence
    /// needs no ordering, because any event at all supersedes it.
    /// </para>
    /// </summary>
    Task RememberNoBasketAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts one more command this gateway has published for a buyer and not yet seen the resulting
    /// event for. While that count is above zero the projection answers
    /// <see cref="ProjectedBasketKind.Unknown"/> for them, so the read that follows their own write
    /// is served from BasketService instead.
    /// <para>
    /// This is the read-your-own-writes guard. Add-to-cart publishes a one-way command and redirects
    /// immediately; without it the redirected page can render from a projection that does not have
    /// the item yet, and "I added it and it vanished" makes the whole design look broken.
    /// </para>
    /// </summary>
    Task NoteWriteInFlightAsync(string buyerId, CancellationToken cancellationToken = default);
}

/// <summary>Which of the three things the projection is able to say about a buyer.</summary>
public enum ProjectedBasketKind
{
    /// <summary>A basket, good enough to render.</summary>
    Basket,

    /// <summary>This buyer has no basket. A normal answer: baskets are created on the first add.</summary>
    NoBasket,

    /// <summary>
    /// Ask BasketService. Either nothing is projected for this buyer, or one of their own writes is
    /// still in flight, or the projection store itself could not be reached.
    /// </summary>
    Unknown
}

public readonly record struct ProjectedBasket(ProjectedBasketKind Kind, BasketSnapshot? Basket)
{
    public static readonly ProjectedBasket Unknown = new(ProjectedBasketKind.Unknown, null);

    public static readonly ProjectedBasket NoBasket = new(ProjectedBasketKind.NoBasket, null);

    public static ProjectedBasket Found(BasketSnapshot basket) => new(ProjectedBasketKind.Basket, basket);
}

/// <summary>
/// How long the three things the projection remembers stay remembered. Shared by every
/// implementation, so there is one place to read what the projection promises rather than one per
/// backing store.
/// </summary>
public static class BasketProjectionLifetimes
{
    /// <summary>
    /// A projected basket. Long enough that no live basket is ever evicted — baskets are
    /// cookie-scoped and deleted at checkout — and short enough that the store does not accumulate
    /// an entry per visitor forever. An evicted entry costs one query to repopulate, nothing more.
    /// </summary>
    public static readonly TimeSpan Entry = TimeSpan.FromDays(7);

    /// <summary>
    /// A cached "this buyer has no basket". Shorter, because it is a claim that stops being true the
    /// moment they add something. The write path makes that safe on its own — a published command
    /// marks the buyer in flight, which forces the next read to BasketService — so this is only a
    /// second line of defence against a basket created by something other than this gateway.
    /// </summary>
    public static readonly TimeSpan NoBasket = TimeSpan.FromMinutes(5);

    /// <summary>
    /// An in-flight write. A backstop, not a wait: the count is normally cleared by the event the
    /// command produced, within milliseconds. This expiry covers the cases where no event is coming
    /// at all — a SetQuantities for a basket that is already checked out, a transfer with nothing to
    /// transfer, or an event BasketService failed to publish. Reads for that buyer are served from
    /// BasketService until it lapses, which is correct but slower, so it is seconds not minutes.
    /// </summary>
    public static readonly TimeSpan InFlightWrite = TimeSpan.FromSeconds(30);
}
