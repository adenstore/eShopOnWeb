using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.eShopWeb.BasketService.Contracts;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Microsoft.eShopWeb.BasketService.IntegrationTests;

/// <summary>
/// Stands in for the API gateway: publishes commands, asks the queries, and watches the event
/// exchange. Speaks only the published contracts, never BasketService's internals, so these tests
/// break if the wire format changes — which is the point.
/// </summary>
public sealed class BasketMessageClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IConnection _connection;
    private readonly IChannel _publishChannel;
    private readonly IChannel _eventChannel;
    private readonly ConcurrentQueue<BasketChangedIntegrationEvent> _events = new();

    private BasketMessageClient(IConnection connection, IChannel publishChannel, IChannel eventChannel)
    {
        _connection = connection;
        _publishChannel = publishChannel;
        _eventChannel = eventChannel;
    }

    public static async Task<BasketMessageClient> ConnectAsync(BasketServiceApplication app)
    {
        var factory = new ConnectionFactory
        {
            HostName = app.RabbitMqHost,
            Port = app.RabbitMqPort,
            UserName = app.RabbitMqUser,
            Password = app.RabbitMqPassword,
            ClientProvidedName = "integration-tests"
        };

        var connection = await factory.CreateConnectionAsync();
        var publishChannel = await connection.CreateChannelAsync();
        var eventChannel = await connection.CreateChannelAsync();

        var client = new BasketMessageClient(connection, publishChannel, eventChannel);
        await client.WaitForServiceAsync();
        await client.WatchEventsAsync();
        return client;
    }

    /// <summary>
    /// Waits until BasketService has declared its topology. The service owns the exchanges and
    /// queues, so publishing before it has attached would hit an exchange that does not exist yet.
    /// </summary>
    private async Task WaitForServiceAsync()
    {
        var ready = await PolledWait.UntilAsync(async () =>
        {
            // Passive declare throws if the queue is absent, which PolledWait reads as "not yet".
            // It needs its own channel: a failed passive declare kills the channel it ran on.
            await using var probe = await _connection.CreateChannelAsync();
            await probe.QueueDeclarePassiveAsync(BasketMessaging.CommandQueue);
            return true;
        });

        if (!ready)
        {
            throw new InvalidOperationException(
                $"BasketService never declared '{BasketMessaging.CommandQueue}'. Is the consumer attaching to the container's broker?");
        }
    }

    private async Task WatchEventsAsync()
    {
        // A subscriber declares the exchange too. Declaring is idempotent, and it means this does
        // not depend on the service having got there first.
        await _eventChannel.ExchangeDeclareAsync(BasketMessaging.EventsExchange, ExchangeType.Fanout, durable: true);

        var queue = (await _eventChannel.QueueDeclareAsync("", durable: false, exclusive: true, autoDelete: true)).QueueName;
        await _eventChannel.QueueBindAsync(queue, BasketMessaging.EventsExchange, routingKey: string.Empty);

        var consumer = new AsyncEventingBasicConsumer(_eventChannel);
        consumer.ReceivedAsync += (_, ea) =>
        {
            var changed = JsonSerializer.Deserialize<BasketChangedIntegrationEvent>(ea.Body.Span, JsonOptions);
            if (changed is not null)
            {
                _events.Enqueue(changed);
            }
            return Task.CompletedTask;
        };
        await _eventChannel.BasicConsumeAsync(queue, autoAck: true, consumer: consumer);
    }

    public IReadOnlyCollection<BasketChangedIntegrationEvent> ObservedEvents => _events.ToArray();

    public void ForgetObservedEvents() => _events.Clear();

    /// <summary>The latest event seen for a buyer, or null if none has arrived yet.</summary>
    public BasketChangedIntegrationEvent? LatestEventFor(string buyerId) =>
        _events.Where(e => e.Basket.BuyerId == buyerId).OrderBy(e => e.OccurredAt).LastOrDefault();

    public Task SendAsync(string routingKey, object command) =>
        _publishChannel.BasicPublishAsync(
            BasketMessaging.CommandExchange, routingKey, mandatory: false,
            basicProperties: new BasicProperties { ContentType = "application/json", Persistent = true },
            body: JsonSerializer.SerializeToUtf8Bytes(command, JsonOptions)).AsTask();

    /// <summary>Publishes a raw body, for the malformed-message cases.</summary>
    public Task SendRawAsync(string routingKey, byte[] body) =>
        _publishChannel.BasicPublishAsync(
            BasketMessaging.CommandExchange, routingKey, mandatory: false,
            basicProperties: new BasicProperties { ContentType = "application/json" },
            body: body).AsTask();

    /// <summary>Publishes an order event, as OrderService would.</summary>
    public async Task PublishOrderEventAsync(object orderEvent, string eventType)
    {
        await using var channel = await _connection.CreateChannelAsync();
        await channel.ExchangeDeclareAsync("order-events", ExchangeType.Fanout, durable: true);
        await channel.BasicPublishAsync("order-events", routingKey: string.Empty, mandatory: false,
            basicProperties: new BasicProperties
            {
                ContentType = "application/json",
                Type = eventType,
                Persistent = true
            },
            body: JsonSerializer.SerializeToUtf8Bytes(orderEvent, JsonOptions));
    }

    /// <summary>
    /// Request/reply over direct reply-to. The request goes out on the very channel holding the
    /// reply-to consumer: on any other channel the broker tears the channel down with
    /// "406 PRECONDITION_FAILED - fast reply consumer does not exist", and the publish call itself
    /// does not throw, so it would look like a request that vanished.
    /// </summary>
    public async Task<BasketQueryReply?> AskAsync(string routingKey, object request)
    {
        await using var channel = await _connection.CreateChannelAsync();

        var correlationId = Guid.NewGuid().ToString();
        var reply = new TaskCompletionSource<BasketQueryReply?>(TaskCreationOptions.RunContinuationsAsynchronously);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, ea) =>
        {
            if (ea.BasicProperties.CorrelationId == correlationId)
            {
                reply.TrySetResult(JsonSerializer.Deserialize<BasketQueryReply>(ea.Body.Span, JsonOptions));
            }
            return Task.CompletedTask;
        };
        await channel.BasicConsumeAsync("amq.rabbitmq.reply-to", autoAck: true, consumer: consumer);

        await channel.BasicPublishAsync(BasketMessaging.CommandExchange, routingKey, mandatory: false,
            basicProperties: new BasicProperties
            {
                ContentType = "application/json",
                CorrelationId = correlationId,
                ReplyTo = "amq.rabbitmq.reply-to"
            },
            body: JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions));

        var finished = await Task.WhenAny(reply.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        return finished == reply.Task ? reply.Task.Result : null;
    }

    public Task<BasketQueryReply?> GetBasketByBuyerAsync(string buyerId) =>
        AskAsync(BasketMessaging.RoutingKeys.GetByBuyer, new { buyerId });

    public Task<BasketQueryReply?> GetBasketByIdAsync(int basketId) =>
        AskAsync(BasketMessaging.RoutingKeys.GetById, new { basketId });

    public async Task<uint> QueueDepthAsync(string queue)
    {
        await using var channel = await _connection.CreateChannelAsync();
        return (await channel.QueueDeclarePassiveAsync(queue)).MessageCount;
    }

    public async ValueTask DisposeAsync()
    {
        await _publishChannel.DisposeAsync();
        await _eventChannel.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
