using System.Text.Json;
using Microsoft.eShopWeb.BasketService.Contracts;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Microsoft.eShopWeb.ApiGateway.Messaging;

public interface IBasketQueryClient
{
    /// <summary>
    /// Asks BasketService for a buyer's basket and waits for the answer.
    /// </summary>
    /// <exception cref="TimeoutException">No reply arrived in time.</exception>
    Task<BasketQueryReply> GetByBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks BasketService for one basket by its own id. This is the checkout read: deliberately not
    /// get-or-create, so a missing basket surfaces as missing rather than as an empty one.
    /// </summary>
    /// <exception cref="TimeoutException">No reply arrived in time.</exception>
    Task<BasketQueryReply> GetByIdAsync(int basketId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Request/reply over AMQP, for the reads the projection cannot answer. This is the one place the
/// gateway blocks on the broker, and it is justified where it is used: checkout builds an order, so
/// stale items or stale prices there are a money bug rather than a cosmetic one, and a read right
/// after the shopper's own write has to show their own write.
/// <para>
/// Correctness comes from the queue, not from a timeout. Commands and queries share
/// <see cref="BasketMessaging.CommandQueue"/>, which has a single consumer with a prefetch of 1, so
/// a query published after a command is always handled after that command has committed. That is
/// what makes falling back to this a real answer rather than a guess.
/// </para>
/// </summary>
public sealed class RabbitMqBasketQueryClient : IBasketQueryClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// A page render is blocked on this, so it is seconds rather than minutes. The queue is FIFO
    /// with one consumer, so a reply that takes longer than this means a backlog, and failing fast
    /// sheds load instead of piling more waiting requests onto it.
    /// </summary>
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Confirmations on the query channel too, for the same reason the command publisher has them,
    /// and one more. Without them a request published to an exchange that does not exist — because
    /// BasketService has never started and owns the topology — is an asynchronous channel error that
    /// <c>BasicPublishAsync</c> never raises, so the question that could not be asked at all is
    /// indistinguishable from one that went unanswered: the caller waits out
    /// <see cref="ReplyTimeout"/> and gets a 504. With them the publish throws at once and the
    /// caller gets a 503, which is both accurate and immediate — verified against a broker with no
    /// "Basket" exchange, where the read route answered 504 after 5s before this and 503 after
    /// milliseconds with it.
    /// </summary>
    private static readonly CreateChannelOptions ChannelOptions = new(
        publisherConfirmationsEnabled: true,
        publisherConfirmationTrackingEnabled: true);

    private readonly IRabbitMqConnection _connection;
    private readonly ILogger<RabbitMqBasketQueryClient> _logger;

    public RabbitMqBasketQueryClient(IRabbitMqConnection connection,
        ILogger<RabbitMqBasketQueryClient> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    public Task<BasketQueryReply> GetByBuyerAsync(string buyerId, CancellationToken cancellationToken = default) =>
        AskAsync(BasketMessaging.RoutingKeys.GetByBuyer, new GetBasketByBuyerRequest { BuyerId = buyerId },
            cancellationToken);

    public Task<BasketQueryReply> GetByIdAsync(int basketId, CancellationToken cancellationToken = default) =>
        AskAsync(BasketMessaging.RoutingKeys.GetById, new GetBasketByIdRequest { BasketId = basketId },
            cancellationToken);

    private async Task<BasketQueryReply> AskAsync(string routingKey, object request,
        CancellationToken cancellationToken)
    {
        // A channel per query, so the request is published on the very channel holding the reply-to
        // consumer. Publish it on any other and the broker tears that channel down with
        // "406 PRECONDITION_FAILED - fast reply consumer does not exist" — and BasicPublishAsync does
        // not throw, so it presents as a request that silently vanished. The alternative, one
        // long-lived channel multiplexing replies by correlation id, saves a channel open per query
        // at the cost of that bookkeeping; worth revisiting only if the projection stops absorbing
        // most reads, since a channel costs an AMQP round-trip and a query costs a service one.
        var connection = await _connection.GetAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(ChannelOptions, cancellationToken);

        var correlationId = Guid.NewGuid().ToString();
        var reply = new TaskCompletionSource<BasketQueryReply>(TaskCreationOptions.RunContinuationsAsynchronously);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) =>
        {
            // Another query's reply cannot arrive on this channel's pseudo-queue, but checking the
            // correlation id is what makes that a fact about the code rather than about the broker.
            if (delivery.BasicProperties.CorrelationId != correlationId)
            {
                return Task.CompletedTask;
            }

            try
            {
                var answer = JsonSerializer.Deserialize<BasketQueryReply>(delivery.Body.Span, JsonOptions);
                reply.TrySetResult(answer ?? BasketQueryReply.Failed("BasketService sent an empty reply"));
            }
            catch (JsonException ex)
            {
                reply.TrySetException(ex);
            }

            return Task.CompletedTask;
        };

        // autoAck: direct reply-to does not support acknowledgements, and there is nothing to
        // redeliver a reply to anyway — the pseudo-queue is gone the moment this channel closes.
        await channel.BasicConsumeAsync(DirectReplyQueue, autoAck: true, consumer: consumer,
            cancellationToken: cancellationToken);

        var properties = new BasicProperties
        {
            ContentType = "application/json",
            CorrelationId = correlationId,
            ReplyTo = DirectReplyQueue,
            Type = request.GetType().Name,
            // Not persistent, unlike the commands: a query whose caller has given up waiting is
            // worthless, so surviving a broker restart would only mean answering into a void.
            Persistent = false
        };

        await channel.BasicPublishAsync(BasketMessaging.CommandExchange, routingKey, mandatory: false,
            basicProperties: properties,
            body: JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions),
            cancellationToken: cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ReplyTimeout);
        try
        {
            return await reply.Task.WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("No reply to {RoutingKey} within {Timeout}s", routingKey, ReplyTimeout.TotalSeconds);
            throw new TimeoutException($"BasketService did not answer {routingKey} within {ReplyTimeout.TotalSeconds}s");
        }
    }

    /// <summary>
    /// RabbitMQ's direct reply-to pseudo-queue. Consuming from it needs no queue to be declared,
    /// which is what keeps the gateway from owning any part of BasketService's topology.
    /// </summary>
    private const string DirectReplyQueue = "amq.rabbitmq.reply-to";
}
