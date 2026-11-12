using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.BasketService.Contracts;

/// <summary>
/// The read side: request/reply over AMQP. The caller publishes to
/// <see cref="BasketMessaging.CommandExchange"/> with <c>ReplyTo</c> set (normally
/// <c>amq.rabbitmq.reply-to</c>, the broker's direct-reply pseudo-queue) and a
/// <c>CorrelationId</c>; the reply is a <see cref="BasketQueryReply"/> carrying the same id.
/// <para>
/// Request/reply is the exception here, not the rule. It is justified for these two because the
/// user is blocked on the answer and staleness is a correctness problem, not a cosmetic one —
/// checkout builds an order, and wrong items or wrong prices are a money bug. It is deliberately
/// not used for the header item count, which renders on every single page: that is served from a
/// projection fed by <see cref="BasketChangedIntegrationEvent"/>, so a page view costs no
/// broker round-trip.
/// </para>
/// <para>
/// WARNING for the calling side: with <c>amq.rabbitmq.reply-to</c> the request has to be published
/// on the very channel that holds the reply-to consumer. Publish it on any other channel and the
/// broker tears that channel down with
/// <c>406 PRECONDITION_FAILED - fast reply consumer does not exist</c> — and
/// <c>BasicPublishAsync</c> does not throw, so it presents as a request that silently vanished.
/// Either keep one channel per in-flight request, or one long-lived channel that both consumes
/// the replies and publishes the requests.
/// </para>
/// </summary>
public record GetBasketByBuyerRequest
{
    [Required]
    public string BuyerId { get; init; } = string.Empty;
}

/// <summary>
/// Looks a basket up by its own id, for checkout. Deliberately *not* get-or-create: creating one
/// here would sail past the caller's null guard and surface a missing basket as "empty basket"
/// instead of "not found".
/// </summary>
public record GetBasketByIdRequest
{
    [Range(1, int.MaxValue)]
    public int BasketId { get; init; }
}

/// <summary>
/// Reply to either request. A buyer with no basket is <see cref="Basket"/> = null with no
/// <see cref="Error"/> — absence is a normal answer, and this service creates baskets lazily on
/// the first add rather than on a read. <see cref="Error"/> is set only when the request itself
/// could not be served, so the caller can tell "you have no basket" from "ask again later".
/// </summary>
public record BasketQueryReply(BasketSnapshot? Basket, string? Error = null)
{
    public static BasketQueryReply Found(BasketSnapshot basket) => new(basket);

    // Basket: null by name, not positionally. A single null argument binds to the record's
    // generated copy constructor instead of this one, which then dereferences it.
    public static BasketQueryReply NotFound() => new(Basket: null);

    public static BasketQueryReply Failed(string error) => new(null, error);
}
