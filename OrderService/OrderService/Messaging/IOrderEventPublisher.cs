using Microsoft.eShopWeb.OrderService.Contracts.Events;

namespace Microsoft.eShopWeb.OrderService.Messaging;

/// <summary>
/// Abstraction over the message broker. Swap <see cref="LoggingOrderEventPublisher"/>
/// for a RabbitMQ implementation in Program.cs without touching the endpoints.
/// </summary>
public interface IOrderEventPublisher
{
    Task PublishAsync(OrderCreatedIntegrationEvent orderCreated, CancellationToken cancellationToken = default);
}
