using System.Text.Json;
using Microsoft.eShopWeb.BasketService.Contracts;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Microsoft.eShopWeb.BasketService.Messaging;

/// <summary>
/// The service's front door: consumes the "basket-service" queue, dispatches on routing key and,
/// for queries, replies to the caller's ReplyTo queue. Transport only — the work is in
/// <see cref="BasketMessageHandler"/>.
/// </summary>
public class BasketCommandConsumer : RabbitMqConsumerService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BasketCommandConsumer> _logger;

    public BasketCommandConsumer(IRabbitMqConnection connection, IServiceScopeFactory scopeFactory,
        ILogger<BasketCommandConsumer> logger)
        : base(connection, logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ConfigureAsync(IChannel channel, CancellationToken cancellationToken)
    {
        await DeclareTopologyAsync(channel, cancellationToken);

        // One unacked message at a time. Without it the broker pushes the whole queue to this one
        // consumer, and a crash redelivers all of it instead of one message. It also keeps the
        // queue strictly FIFO through the handler, which is what lets a query published after a
        // command see that command's result — see BasketMessaging.
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += OnReceivedAsync;

        await channel.BasicConsumeAsync(BasketMessaging.CommandQueue, autoAck: false, consumer: consumer,
            cancellationToken: cancellationToken);

        _logger.LogInformation("Consuming {Queue} on exchange {Exchange}",
            BasketMessaging.CommandQueue, BasketMessaging.CommandExchange);
    }

    private static async Task DeclareTopologyAsync(IChannel channel, CancellationToken cancellationToken)
    {
        // An unroutable message — one published with a routing key nothing is bound to — is dropped
        // silently by default. The alternate exchange turns that into something you can look at.
        await channel.ExchangeDeclareAsync(BasketMessaging.UnroutableExchange, ExchangeType.Fanout,
            durable: true, autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(BasketMessaging.UnroutableQueue, durable: true, exclusive: false,
            autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(BasketMessaging.UnroutableQueue, BasketMessaging.UnroutableExchange,
            routingKey: string.Empty, cancellationToken: cancellationToken);

        // Where rejected messages land: malformed JSON, failed validation, unhandled errors.
        await channel.ExchangeDeclareAsync(BasketMessaging.InvalidExchange, ExchangeType.Fanout,
            durable: true, autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(BasketMessaging.InvalidQueue, durable: true, exclusive: false,
            autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(BasketMessaging.InvalidQueue, BasketMessaging.InvalidExchange,
            routingKey: string.Empty, cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync(BasketMessaging.CommandExchange, ExchangeType.Topic,
            durable: true, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["alternate-exchange"] = BasketMessaging.UnroutableExchange },
            cancellationToken: cancellationToken);

        // Declared here as well as by the publisher, so the service owns its whole topology from
        // startup. The publisher only creates this on the first event, which would leave a
        // subscriber that connects before anyone's basket changes unable to bind to it at all.
        await channel.ExchangeDeclareAsync(BasketMessaging.EventsExchange, ExchangeType.Fanout,
            durable: true, autoDelete: false, cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(BasketMessaging.CommandQueue, durable: true, exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-dead-letter-exchange"] = BasketMessaging.InvalidExchange },
            cancellationToken: cancellationToken);

        foreach (var routingKey in new[]
                 {
                     BasketMessaging.RoutingKeys.AddItem,
                     BasketMessaging.RoutingKeys.SetQuantities,
                     BasketMessaging.RoutingKeys.Transfer,
                     BasketMessaging.RoutingKeys.GetByBuyer,
                     BasketMessaging.RoutingKeys.GetById
                 })
        {
            await channel.QueueBindAsync(BasketMessaging.CommandQueue, BasketMessaging.CommandExchange,
                routingKey, cancellationToken: cancellationToken);
        }
    }

    private async Task OnReceivedAsync(object sender, BasicDeliverEventArgs ea)
    {
        var channel = Channel;
        var routingKey = ea.RoutingKey;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<BasketMessageHandler>();

            BasketQueryReply? reply = null;

            switch (routingKey)
            {
                case BasketMessaging.RoutingKeys.AddItem:
                    if (!TryRead<AddItemToBasketCommand>(ea, out var addItem, out var addItemError))
                    {
                        await RejectAsync(ea, routingKey, addItemError);
                        return;
                    }
                    await handler.HandleAsync(addItem);
                    break;

                case BasketMessaging.RoutingKeys.SetQuantities:
                    if (!TryRead<SetBasketQuantitiesCommand>(ea, out var setQuantities, out var setQuantitiesError))
                    {
                        await RejectAsync(ea, routingKey, setQuantitiesError);
                        return;
                    }
                    await handler.HandleAsync(setQuantities);
                    break;

                case BasketMessaging.RoutingKeys.Transfer:
                    if (!TryRead<TransferBasketCommand>(ea, out var transfer, out var transferError))
                    {
                        await RejectAsync(ea, routingKey, transferError);
                        return;
                    }
                    await handler.HandleAsync(transfer);
                    break;

                case BasketMessaging.RoutingKeys.GetByBuyer:
                    if (!TryRead<GetBasketByBuyerRequest>(ea, out var byBuyer, out var byBuyerError))
                    {
                        // A bad query still gets an answer: the caller is blocked waiting on one,
                        // and dead-lettering in silence would leave it to time out.
                        reply = BasketQueryReply.Failed(byBuyerError);
                        break;
                    }
                    reply = await handler.HandleAsync(byBuyer);
                    break;

                case BasketMessaging.RoutingKeys.GetById:
                    if (!TryRead<GetBasketByIdRequest>(ea, out var byId, out var byIdError))
                    {
                        reply = BasketQueryReply.Failed(byIdError);
                        break;
                    }
                    reply = await handler.HandleAsync(byId);
                    break;

                default:
                    // Bound to this queue but unknown here — a newer publisher, most likely.
                    await RejectAsync(ea, routingKey, $"no handler for routing key '{routingKey}'");
                    return;
            }

            if (reply is not null)
            {
                await PublishReplyAsync(ea, reply);
            }

            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            // requeue: false on purpose. Redelivering a command that threw halfway would re-apply
            // whatever part of it already committed, and a message that fails deterministically
            // would spin forever. It goes to the "invalid" queue instead, where it can be read.
            _logger.LogError(ex, "Handling {RoutingKey} failed; dead-lettering message", routingKey);
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
        }
    }

    /// <summary>Deserializes and validates in one step; both failures dead-letter the same way.</summary>
    private static bool TryRead<T>(BasicDeliverEventArgs ea, out T message, out string error) where T : class
    {
        message = null!;
        try
        {
            var deserialized = JsonSerializer.Deserialize<T>(ea.Body.Span, JsonOptions);
            if (deserialized is null)
            {
                error = $"body deserialized to null {typeof(T).Name}";
                return false;
            }
            if (!MessageValidator.TryValidate(deserialized, out error))
            {
                return false;
            }
            message = deserialized;
            return true;
        }
        catch (JsonException ex)
        {
            error = $"malformed {typeof(T).Name}: {ex.Message}";
            return false;
        }
    }

    private async Task RejectAsync(BasicDeliverEventArgs ea, string routingKey, string reason)
    {
        _logger.LogWarning("Rejected {RoutingKey}: {Reason}", routingKey, reason);
        await Channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
    }

    /// <summary>
    /// Sends a query reply to the caller's ReplyTo queue, correlated by CorrelationId. Published on
    /// its own short-lived channel rather than the consuming one, for the same reason the event
    /// publisher does: not re-entering the channel that is mid-delivery.
    /// </summary>
    private async Task PublishReplyAsync(BasicDeliverEventArgs ea, BasketQueryReply reply)
    {
        var replyTo = ea.BasicProperties.ReplyTo;
        if (string.IsNullOrEmpty(replyTo))
        {
            _logger.LogWarning("Query {RoutingKey} arrived with no ReplyTo; dropping the reply", ea.RoutingKey);
            return;
        }

        var connection = await Connection.GetAsync();
        await using var channel = await connection.CreateChannelAsync();

        var properties = new BasicProperties
        {
            ContentType = "application/json",
            CorrelationId = ea.BasicProperties.CorrelationId,
            Type = nameof(BasketQueryReply)
        };

        // Replies go straight to the named queue via the default exchange, and are not persistent:
        // a reply nobody is still waiting for is worthless, and amq.rabbitmq.reply-to is gone the
        // moment the caller disconnects.
        await channel.BasicPublishAsync(exchange: string.Empty, routingKey: replyTo, mandatory: false,
            basicProperties: properties,
            body: JsonSerializer.SerializeToUtf8Bytes(reply, JsonOptions));
    }
}
