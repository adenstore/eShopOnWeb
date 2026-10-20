namespace Microsoft.eShopWeb.OrderService.Contracts.Events;

/// <summary>
/// Published by the Order microservice after an order has been saved.
/// This is the message other services subscribe to (e.g. via RabbitMQ).
/// </summary>
public record OrderCreatedIntegrationEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    int OrderId,
    string BuyerId,
    decimal Total,
    List<OrderItemDto> Items);
