using System.Text.Json;
using Microsoft.eShopWeb.ApiGateway.Projections;
using Microsoft.eShopWeb.BasketService.Contracts;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Microsoft.eShopWeb.ApiGateway.Messaging;

/// <summary>
/// Keeps the gateway's basket projection up to date from the "basket-events" fanout. This is the
/// other half of serving basket reads without a broker round-trip: the write path publishes
/// commands, BasketService announces the result, and this applies it so the next page render is a
/// local lookup.
/// </summary>
public sealed class BasketEventsConsumer : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The gateway's own queue on the fanout — named after the reader, like BasketService's
    /// "basket-order-events". Durable and not auto-delete, so events wait here while the gateway is
    /// restarting and the projection catches up instead of going stale until each basket's next
    /// change.
    /// </summary>
    private const string QueueName = "api-gateway-basket-events";

    /// <summary>
    /// Bounds that backlog, because a queue nobody is draining is how a broker fills its disk. It is
    /// safe to drop the *oldest* specifically because the events carry full baskets rather than
    /// deltas: the newest event for a basket is the whole truth about it, so an older one is pure
    /// redundancy. The limit it costs is the self-healing property — a basket whose only event was
    /// dropped stays missing from the projection until it changes again, and reads for it fall
    /// through to a query in the meantime, which is correct but slower.
    /// </summary>
    private static readonly Dictionary<string, object?> QueueArguments = new()
    {
        ["x-max-length"] = 10_000,
        ["x-overflow"] = "drop-head"
    };

    private readonly IRabbitMqConnection _connection;
    private readonly IBasketProjection _projection;
    private readonly ILogger<BasketEventsConsumer> _logger;

    public BasketEventsConsumer(IRabbitMqConnection connection, IBasketProjection projection,
        ILogger<BasketEventsConsumer> logger)
    {
        _connection = connection;
        _projection = projection;
        _logger = logger;
    }

    /// <summary>
    /// Retries the initial attach instead of taking the host down with it. An exception out of
    /// <see cref="BackgroundService.ExecuteAsync"/> stops the whole host by default, and a gateway
    /// that refuses to boot because the broker is not up yet would also stop proxying the services
    /// that have nothing to do with RabbitMQ. <c>depends_on</c> waits for the container to start,
    /// not for RabbitMQ to finish booting, so that race is routine.
    /// <para>
    /// Only the first attach is retried here: once attached, RabbitMQ.Client recovers a dropped
    /// connection, its channels, the topology and the consumers by itself, so re-running the setup
    /// on every blip would duplicate consumers rather than help.
    /// </para>
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = FirstRetryDelay;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await AttachAsync(stoppingToken);

                // Attached. The consumer callback drives everything from here.
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not attach to {Exchange}; retrying in {Delay}s",
                    BasketMessaging.EventsExchange, delay.TotalSeconds);

                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxRetryDelay.Ticks));
            }
        }
    }

    private async Task AttachAsync(CancellationToken cancellationToken)
    {
        var connection = await _connection.GetAsync(cancellationToken);
        var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        // The subscriber declares the exchange too, identically to BasketService's publisher
        // (fanout, durable, no arguments). Declaring is idempotent, so it does not matter which
        // starts first — and unlike the "Basket" command exchange, this one carries no arguments to
        // get out of step with.
        await channel.ExchangeDeclareAsync(BasketMessaging.EventsExchange, ExchangeType.Fanout,
            durable: true, autoDelete: false, cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(QueueName, durable: true, exclusive: false, autoDelete: false,
            arguments: QueueArguments, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(QueueName, BasketMessaging.EventsExchange, routingKey: string.Empty,
            cancellationToken: cancellationToken);

        // One at a time. Events for one basket are published in order, and applying them one at a
        // time keeps that order through the projection, so the newer-wins check is a guard against
        // redelivery rather than the thing doing the ordering.
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false,
            cancellationToken: cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => OnReceivedAsync(channel, delivery);

        await channel.BasicConsumeAsync(QueueName, autoAck: false, consumer: consumer,
            cancellationToken: cancellationToken);

        _logger.LogInformation("Projecting basket events from queue {Queue}", QueueName);
    }

    private async Task OnReceivedAsync(IChannel channel, BasicDeliverEventArgs delivery)
    {
        BasketChangedIntegrationEvent? changed;
        try
        {
            changed = JsonSerializer.Deserialize<BasketChangedIntegrationEvent>(delivery.Body.Span, JsonOptions);
        }
        catch (JsonException ex)
        {
            // Will never succeed, so requeueing it would spin forever. Drop it: the next event for
            // that basket repairs the projection.
            _logger.LogError(ex, "Could not read a basket event; dropping it");
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false);
            return;
        }

        if (changed is null)
        {
            _logger.LogError("Empty basket event; dropping it");
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false);
            return;
        }

        try
        {
            await _projection.ApplyAsync(changed);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            // The projection store is unreachable, most likely. Requeue: applying a full-state event
            // twice is a no-op, so a retry is free, and the alternative is a projection that is
            // quietly wrong.
            _logger.LogError(ex, "Could not project the basket event for {BuyerId}; requeueing it",
                changed.Basket.BuyerId);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true);
        }
    }
}
