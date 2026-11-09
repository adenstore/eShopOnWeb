using Microsoft.eShopWeb.LagerService.Data;
using Microsoft.eShopWeb.OrderService.Contracts.Events;

namespace Microsoft.eShopWeb.LagerService.Messaging;

/// <summary>
/// Takes the ordered items out of the stock. Kept apart from the RabbitMQ code so it can be tested without a broker.
/// </summary>
public class OrderCreatedHandler
{
    private readonly IStockRepository _stock;
    private readonly ILogger<OrderCreatedHandler> _logger;

    public OrderCreatedHandler(IStockRepository stock, ILogger<OrderCreatedHandler> logger)
    {
        _stock = stock;
        _logger = logger;
    }

    public async Task HandleAsync(OrderCreatedIntegrationEvent orderCreated)
    {
        // RabbitMQ delivers "at least once", so the same event can arrive twice. Only count it once.
        if (!await _stock.TryMarkEventHandledAsync(orderCreated.EventId))
        {
            _logger.LogInformation("Event {EventId} for order {OrderId} was already handled, skipping",
                orderCreated.EventId, orderCreated.OrderId);
            return;
        }

        foreach (var item in orderCreated.Items)
        {
            var result = await _stock.DecreaseAsync(item.CatalogItemId, item.Units);
            switch (result.Status)
            {
                case DecreaseStatus.Ok:
                    _logger.LogInformation("Order {OrderId}: took {Units} of item {CatalogItemId}, {Quantity} left",
                        orderCreated.OrderId, item.Units, item.CatalogItemId, result.Quantity);
                    break;
                case DecreaseStatus.NotFound:
                    _logger.LogWarning("Order {OrderId}: item {CatalogItemId} is not in the stock",
                        orderCreated.OrderId, item.CatalogItemId);
                    break;
                case DecreaseStatus.InsufficientStock:
                    // The order is already placed, so the stock service can only report it.
                    _logger.LogWarning("Order {OrderId}: wanted {Units} of item {CatalogItemId} but only {Quantity} in stock",
                        orderCreated.OrderId, item.Units, item.CatalogItemId, result.Quantity);
                    break;
            }
        }
    }
}
