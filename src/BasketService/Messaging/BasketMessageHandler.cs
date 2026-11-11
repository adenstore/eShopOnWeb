using Microsoft.eShopWeb.BasketService.Contracts;
using Microsoft.eShopWeb.BasketService.Interfaces;
using Microsoft.eShopWeb.OrderService.Contracts.Events;

namespace Microsoft.eShopWeb.BasketService.Messaging;

/// <summary>
/// What each message actually does, with no AMQP in sight. The consumers above this are pure
/// transport — deserialize, dispatch here, ack or dead-letter — which keeps every rule in this
/// class reachable from a unit test without a broker.
/// </summary>
public class BasketMessageHandler
{
    private readonly IBasketService _basketService;
    private readonly IBasketEventPublisher _events;
    private readonly ILogger<BasketMessageHandler> _logger;

    public BasketMessageHandler(IBasketService basketService, IBasketEventPublisher events,
        ILogger<BasketMessageHandler> logger)
    {
        _basketService = basketService;
        _events = events;
        _logger = logger;
    }

    public async Task HandleAsync(AddItemToBasketCommand command, CancellationToken cancellationToken = default)
    {
        var basket = await _basketService.AddItemToBasket(
            command.BuyerId, command.CatalogItemId, command.UnitPrice, command.Quantity);

        await _events.PublishBasketChangedAsync(BasketSnapshot.FromBasket(basket), cancellationToken);
    }

    public async Task HandleAsync(SetBasketQuantitiesCommand command, CancellationToken cancellationToken = default)
    {
        var result = await _basketService.SetQuantities(command.BasketId, command.Quantities);

        if (!result.IsSuccess)
        {
            // Nothing to retry and no caller to tell: the basket is gone (most likely already
            // checked out). Log and let the consumer ack, rather than dead-lettering a message that
            // would fail identically forever.
            _logger.LogWarning("SetQuantities ignored: basket {BasketId} not found", command.BasketId);
            return;
        }

        await _events.PublishBasketChangedAsync(BasketSnapshot.FromBasket(result.Value), cancellationToken);
    }

    public async Task HandleAsync(TransferBasketCommand command, CancellationToken cancellationToken = default)
    {
        // Read the anonymous basket first, for its id: the transfer deletes it, and the event for
        // it has to go out under the id it had.
        var anonymousBasket = await _basketService.GetBasketByBuyerAsync(command.AnonymousId);
        if (anonymousBasket is null)
        {
            _logger.LogInformation("Nothing to transfer: no basket for anonymous id {AnonymousId}", command.AnonymousId);
            return;
        }
        var anonymousBasketId = anonymousBasket.Id;

        await _basketService.TransferBasketAsync(command.AnonymousId, command.UserName);

        // Two baskets changed: the anonymous one is gone and the user's absorbed its items. A
        // reader keyed by buyer needs both events, or the anonymous basket's items sit in its
        // projection forever under the cookie id.
        var userBasket = await _basketService.GetBasketByBuyerAsync(command.UserName);
        if (userBasket is not null)
        {
            await _events.PublishBasketChangedAsync(BasketSnapshot.FromBasket(userBasket), cancellationToken);
        }
        await _events.PublishBasketChangedAsync(
            BasketSnapshot.Empty(anonymousBasketId, command.AnonymousId), cancellationToken);
    }

    /// <summary>
    /// Clears the basket once an order has been placed. This is why the checkout page has no
    /// "delete basket" call to make: the old flow was SetQuantities → CreateOrder → DeleteBasket,
    /// three calls across two datastores with no atomicity, where a crash could leave an order
    /// placed and the basket still full. Reacting to the order event instead means the basket is
    /// cleared exactly when an order really exists, and a redelivery is a harmless no-op.
    /// </summary>
    public async Task HandleAsync(OrderCreatedIntegrationEvent orderCreated, CancellationToken cancellationToken = default)
    {
        var basket = await _basketService.GetBasketByBuyerAsync(orderCreated.BuyerId);
        if (basket is null)
        {
            _logger.LogInformation("Order {OrderId}: buyer {BuyerId} has no basket to clear (already handled)",
                orderCreated.OrderId, orderCreated.BuyerId);
            return;
        }

        var basketId = basket.Id;
        await _basketService.DeleteBasketAsync(basketId);
        _logger.LogInformation("Order {OrderId}: cleared basket {BasketId} for buyer {BuyerId}",
            orderCreated.OrderId, basketId, orderCreated.BuyerId);

        await _events.PublishBasketChangedAsync(
            BasketSnapshot.Empty(basketId, orderCreated.BuyerId), cancellationToken);
    }

    public async Task<BasketQueryReply> HandleAsync(GetBasketByBuyerRequest request, CancellationToken cancellationToken = default)
    {
        var basket = await _basketService.GetBasketByBuyerAsync(request.BuyerId);

        return basket is null
            ? BasketQueryReply.NotFound()
            : BasketQueryReply.Found(BasketSnapshot.FromBasket(basket));
    }

    public async Task<BasketQueryReply> HandleAsync(GetBasketByIdRequest request, CancellationToken cancellationToken = default)
    {
        var basket = await _basketService.GetBasketAsync(request.BasketId);

        return basket is null
            ? BasketQueryReply.NotFound()
            : BasketQueryReply.Found(BasketSnapshot.FromBasket(basket));
    }
}
