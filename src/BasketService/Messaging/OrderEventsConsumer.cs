using System.Text.Json;
using Microsoft.eShopWeb.BasketService.Contracts;
using Microsoft.eShopWeb.OrderService.Contracts.Events;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Microsoft.eShopWeb.BasketService.Messaging;

/// <summary>
/// Service-to-service subscription: binds a queue of our own to OrderService's "order-events"
/// fanout and clears the basket when an order is placed. OrderService knows nothing about this —
/// it publishes to a fanout and we choose to listen, which is the point of the exchange.
/// </summary>
public class OrderEventsConsumer : RabbitMqConsumerService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OrderEventsConsumer> _logger;

    public OrderEventsConsumer(IRabbitMqConnection connection, IServiceScopeFactory scopeFactory,
        ILogger<OrderEventsConsumer> logger)
        : base(connection, logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ConfigureAsync(IChannel channel, CancellationToken cancellationToken)
    {
        // Declared here too, not just by OrderService: whichever service starts first creates it,
        // and declaring is idempotent as long as the arguments agree (fanout, durable).
        await channel.ExchangeDeclareAsync(OrderEventsExchange.Name, ExchangeType.Fanout, durable: true,
            autoDelete: false, cancellationToken: cancellationToken);

        // Our own durable queue, so orders placed while this service is down are still waiting for
        // it on restart. A second subscriber to order-events would declare its own queue instead of
        // sharing this one, and get its own copy of every event.
        await channel.QueueDeclareAsync(BasketMessaging.OrderEventsQueue, durable: true, exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-dead-letter-exchange"] = BasketMessaging.InvalidExchange },
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(BasketMessaging.OrderEventsQueue, OrderEventsExchange.Name,
            routingKey: string.Empty, cancellationToken: cancellationToken);

        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += OnReceivedAsync;

        await channel.BasicConsumeAsync(BasketMessaging.OrderEventsQueue, autoAck: false, consumer: consumer,
            cancellationToken: cancellationToken);

        _logger.LogInformation("Consuming {Queue}, bound to {Exchange}",
            BasketMessaging.OrderEventsQueue, OrderEventsExchange.Name);
    }

    private async Task OnReceivedAsync(object sender, BasicDeliverEventArgs ea)
    {
        var channel = Channel;

        // The fanout carries every order event, and the Type property is how the publisher says
        // which. Anything else is somebody else's message: ack it rather than dead-lettering, or we
        // would fill the invalid queue with events that are simply not ours.
        var eventName = ea.BasicProperties.Type;
        if (eventName is not null && eventName != nameof(OrderCreatedIntegrationEvent))
        {
            _logger.LogDebug("Ignoring order event {EventName}", eventName);
            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
            return;
        }

        try
        {
            var orderCreated = JsonSerializer.Deserialize<OrderCreatedIntegrationEvent>(ea.Body.Span, JsonOptions);
            if (orderCreated is null || string.IsNullOrEmpty(orderCreated.BuyerId))
            {
                _logger.LogWarning("Rejected OrderCreated: no buyer id to clear a basket for");
                await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<BasketMessageHandler>();
            await handler.HandleAsync(orderCreated);

            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Handling OrderCreated failed; dead-lettering message");
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
        }
    }

}
