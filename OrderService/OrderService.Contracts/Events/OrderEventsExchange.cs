namespace Microsoft.eShopWeb.OrderService.Contracts.Events;

/// <summary>
/// OrderService publishes all its events to this RabbitMQ fanout exchange.
/// A service that wants them declares its own queue and binds it to the exchange;
/// the message's Type property holds the event's class name (e.g. "OrderCreatedIntegrationEvent").
/// </summary>
public static class OrderEventsExchange
{
    public const string Name = "order-events";
}
