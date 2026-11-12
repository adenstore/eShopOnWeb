namespace Microsoft.eShopWeb.BasketService.Contracts;

/// <summary>
/// The RabbitMQ topology BasketService owns. Callers (the gateway) need these names to publish,
/// so they live in Contracts next to the message shapes rather than in the messaging internals.
/// <para>
/// Commands and queries share one queue on purpose. The broker delivers a queue in FIFO order,
/// and <see cref="CommandQueue"/> has a single consumer with a prefetch of 1, so a query published
/// after a command is always handled after that command has been committed. That is what makes a
/// read-after-write ("I added an item, show me my basket") return the item without any
/// version-stamping or waiting on the read side.
/// </para>
/// </summary>
public static class BasketMessaging
{
    /// <summary>Topic exchange the gateway publishes commands and queries to.</summary>
    public const string CommandExchange = "Basket";

    /// <summary>The single work queue behind <see cref="CommandExchange"/>.</summary>
    public const string CommandQueue = "basket-service";

    /// <summary>
    /// Dead-letter target for messages this service rejected: malformed JSON, a command that
    /// failed validation, or an unhandled error. Bound to <see cref="InvalidQueue"/>.
    /// </summary>
    public const string InvalidExchange = "Basket.invalid";
    public const string InvalidQueue = "invalid";

    /// <summary>
    /// Alternate exchange for <see cref="CommandExchange"/>: catches messages published with a
    /// routing key nothing is bound to, which would otherwise be dropped silently.
    /// </summary>
    public const string UnroutableExchange = "Basket.unroutable";
    public const string UnroutableQueue = "unroutable-messages";

    /// <summary>
    /// Fanout exchange carrying <see cref="BasketChangedIntegrationEvent"/>. Named to match the
    /// "catalog-events" and "order-events" exchanges the other services publish to. A reader
    /// declares its own queue and binds it here.
    /// </summary>
    public const string EventsExchange = "basket-events";

    /// <summary>Queue bound to OrderService's "order-events" fanout, so a placed order clears the basket.</summary>
    public const string OrderEventsQueue = "basket-order-events";

    public static class RoutingKeys
    {
        // Writes: one-way, no reply. Nothing the caller needs comes back.
        public const string AddItem = "Basket.addItem";
        public const string SetQuantities = "Basket.setQuantities";
        public const string Transfer = "Basket.transfer";

        // Reads: request/reply over ReplyTo + CorrelationId.
        public const string GetByBuyer = "Basket.getByBuyer";
        public const string GetById = "Basket.getById";
    }
}
