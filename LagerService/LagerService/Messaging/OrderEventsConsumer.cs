using System.Text.Json;
using Microsoft.eShopWeb.OrderService.Contracts.Events;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Microsoft.eShopWeb.LagerService.Messaging;

/// <summary>
/// Listens on RabbitMQ for the events OrderService publishes to the "order-events" fanout exchange.
/// LagerService has its own durable queue bound to that exchange (publish/subscribe), so messages wait
/// in the queue while the service is down and are handled when it starts again.
/// </summary>
public sealed class OrderEventsConsumer : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private readonly RabbitMqSettings _settings;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OrderEventsConsumer> _logger;

    public OrderEventsConsumer(IOptions<RabbitMqSettings> settings, IServiceScopeFactory scopeFactory, ILogger<OrderEventsConsumer> logger)
    {
        _settings = settings.Value;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // RabbitMQ may start after this service (docker compose), so keep trying until it answers.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lost or could not open the connection to RabbitMQ at {HostName}:{Port}, trying again in {Delay}",
                    _settings.HostName, _settings.Port, RetryDelay);
            }

            try
            {
                await Task.Delay(RetryDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _settings.HostName,
            Port = _settings.Port,
            UserName = _settings.UserName,
            Password = _settings.Password,
            ClientProvidedName = "LagerService"
        };

        await using var connection = await factory.CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        // Same exchange declaration as OrderService, so it does not matter which service starts first.
        await channel.ExchangeDeclareAsync(OrderEventsExchange.Name, ExchangeType.Fanout, durable: true, cancellationToken: stoppingToken);
        await channel.QueueDeclareAsync(_settings.QueueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        await channel.QueueBindAsync(_settings.QueueName, OrderEventsExchange.Name, routingKey: string.Empty, cancellationToken: stoppingToken);
        // Handle one message at a time.
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            try
            {
                if (delivery.BasicProperties.Type == nameof(OrderCreatedIntegrationEvent))
                {
                    var orderCreated = JsonSerializer.Deserialize<OrderCreatedIntegrationEvent>(delivery.Body.Span, JsonOptions)
                        ?? throw new JsonException("Empty message");

                    using var scope = _scopeFactory.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<OrderCreatedHandler>().HandleAsync(orderCreated);
                }
                // Other event types on the exchange do not affect the stock; ack them so they leave the queue.
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false);
            }
            catch (JsonException ex)
            {
                // A message that cannot be read will never succeed, so drop it instead of retrying forever.
                _logger.LogError(ex, "Could not read message {MessageId}, dropping it", delivery.BasicProperties.MessageId);
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false);
            }
            catch (Exception ex)
            {
                // E.g. Redis is down: put the message back so it is tried again.
                _logger.LogError(ex, "Could not handle message {MessageId}, requeueing it", delivery.BasicProperties.MessageId);
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true);
            }
        };

        await channel.BasicConsumeAsync(_settings.QueueName, autoAck: false, consumer, stoppingToken);
        _logger.LogInformation("Listening for order events on queue {Queue}", _settings.QueueName);

        // Keep the connection open until the service stops, or return (and reconnect) if RabbitMQ closes it.
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.ConnectionShutdownAsync += (_, _) =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };
        using (stoppingToken.Register(() => closed.TrySetCanceled(stoppingToken)))
        {
            await closed.Task;
        }
        _logger.LogWarning("The connection to RabbitMQ was closed");
    }
}
