using Microsoft.eShopWeb.OrderService.Contracts.Events;

namespace Microsoft.eShopWeb.OrderService.Messaging;

/// <summary>
/// Abstraction over the message broker. <see cref="RabbitMqOrderEventPublisher"/> is used when the
/// service runs; the tests swap in <see cref="LoggingOrderEventPublisher"/> so they need no broker.
/// </summary>
public interface IOrderEventPublisher
{
    Task PublishAsync(OrderCreatedIntegrationEvent orderCreated, CancellationToken cancellationToken = default);
}
