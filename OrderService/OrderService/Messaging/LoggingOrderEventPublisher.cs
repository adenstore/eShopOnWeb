using Microsoft.eShopWeb.OrderService.Contracts.Events;

namespace Microsoft.eShopWeb.OrderService.Messaging;

/// <summary>
/// Placeholder publisher that only logs the event. Replace with a RabbitMQ publisher.
/// </summary>
public class LoggingOrderEventPublisher : IOrderEventPublisher
{
    private readonly ILogger<LoggingOrderEventPublisher> _logger;

    public LoggingOrderEventPublisher(ILogger<LoggingOrderEventPublisher> logger)
    {
        _logger = logger;
    }

    public Task PublishAsync(OrderCreatedIntegrationEvent orderCreated, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("OrderCreated event for order {OrderId} (buyer {BuyerId}, total {Total})",
            orderCreated.OrderId, orderCreated.BuyerId, orderCreated.Total);
        return Task.CompletedTask;
    }
}
