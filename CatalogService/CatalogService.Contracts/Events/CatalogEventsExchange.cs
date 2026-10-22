namespace Microsoft.eShopWeb.CatalogService.Contracts.Events;

/// <summary>
/// CatalogService publishes all its events to this RabbitMQ fanout exchange.
/// A service that wants them declares its own queue and binds it to the exchange;
/// the message's Type property holds the event's class name (e.g. "CatalogItemUpdatedIntegrationEvent").
/// </summary>
public static class CatalogEventsExchange
{
    public const string Name = "catalog-events";
}
