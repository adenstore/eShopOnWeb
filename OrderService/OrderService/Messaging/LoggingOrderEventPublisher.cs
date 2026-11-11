using Microsoft.eShopWeb.OrderService.Contracts.Events;

namespace Microsoft.eShopWeb.OrderService.Messaging;

/// <summary>
/// Publisher that only logs the event. Used by the tests instead of <see cref="RabbitMqOrderEventPublisher"/>.
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
