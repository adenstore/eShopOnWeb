using System.Text.Json;
using Microsoft.eShopWeb.CatalogService.Contracts.Events;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Microsoft.eShopWeb.Web.Messaging;

/// <summary>
/// Listens to CatalogService's events on RabbitMQ and hands them to <see cref="CatalogEventSync"/>,
/// which keeps the monolith's CatalogDb in sync (see that class for why this exists and when to remove it).
///
/// The Web app has its own durable queue bound to the "catalog-events" fanout exchange, so it receives
/// every catalog event, also those published while the Web app was not running.
/// If RabbitMQ can't be reached, the Web app still starts and this listener keeps retrying.
/// </summary>
public sealed class CatalogEventsListener : BackgroundService
{
    public const string QueueName = "web.catalog-sync";
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly RabbitMqSettings _settings;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CatalogEventsListener> _logger;

    public CatalogEventsListener(IOptions<RabbitMqSettings> settings, IServiceScopeFactory scopeFactory, ILogger<CatalogEventsListener> logger)
    {
        _settings = settings.Value;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the Web app finish starting before connecting.
        await Task.Yield();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ListenUntilDisconnectedAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Catalog sync: no connection to RabbitMQ at {HostName}:{Port}, retrying in {RetryDelay}",
                    _settings.HostName, _settings.Port, RetryDelay);
            }

            try
            {
                await Task.Delay(RetryDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ListenUntilDisconnectedAsync(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _settings.HostName,
            Port = _settings.Port,
            UserName = _settings.UserName,
            Password = _settings.Password,
            ClientProvidedName = "eShopWeb catalog sync",
            // Reconnecting is handled by the loop in ExecuteAsync instead.
            AutomaticRecoveryEnabled = false
        };

        await using var connection = await factory.CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        // Same settings as CatalogService uses, so whichever side starts first creates the exchange.
        await channel.ExchangeDeclareAsync(CatalogEventsExchange.Name, ExchangeType.Fanout, durable: true, cancellationToken: stoppingToken);
        await channel.QueueDeclareAsync(QueueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        await channel.QueueBindAsync(QueueName, CatalogEventsExchange.Name, routingKey: string.Empty, cancellationToken: stoppingToken);
        // One message at a time, so events are applied in the order they were published.
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.ConnectionShutdownAsync += (_, _) =>
        {
            disconnected.TrySetResult();
            return Task.CompletedTask;
        };

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, message) => HandleMessageAsync(channel, message, stoppingToken);
        await channel.BasicConsumeAsync(QueueName, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);

        _logger.LogInformation("Catalog sync: listening on queue {Queue} (exchange {Exchange})", QueueName, CatalogEventsExchange.Name);

        await Task.WhenAny(disconnected.Task, Task.Delay(Timeout.Infinite, stoppingToken));
        stoppingToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException("The connection to RabbitMQ was closed");
    }

    private async Task HandleMessageAsync(IChannel channel, BasicDeliverEventArgs message, CancellationToken stoppingToken)
    {
        var eventType = message.BasicProperties.Type;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var sync = scope.ServiceProvider.GetRequiredService<CatalogEventSync>();

            switch (eventType)
            {
                case nameof(CatalogItemCreatedIntegrationEvent):
                    await sync.ApplyAsync(Deserialize<CatalogItemCreatedIntegrationEvent>(message), stoppingToken);
                    break;
                case nameof(CatalogItemUpdatedIntegrationEvent):
                    await sync.ApplyAsync(Deserialize<CatalogItemUpdatedIntegrationEvent>(message), stoppingToken);
                    break;
                case nameof(CatalogItemDeletedIntegrationEvent):
                    await sync.ApplyAsync(Deserialize<CatalogItemDeletedIntegrationEvent>(message), stoppingToken);
                    break;
                default:
                    _logger.LogWarning("Catalog sync: ignoring unknown event type {EventType}", eventType);
                    break;
            }

            await channel.BasicAckAsync(message.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Not requeued, so one bad message can't block the queue. It is lost; a dead-letter queue
            // would keep it for inspection instead.
            _logger.LogError(ex, "Catalog sync: could not apply {EventType}, message discarded", eventType);
            await channel.BasicNackAsync(message.DeliveryTag, multiple: false, requeue: false, cancellationToken: stoppingToken);
        }
    }

    private static TEvent Deserialize<TEvent>(BasicDeliverEventArgs message) =>
        JsonSerializer.Deserialize<TEvent>(message.Body.Span, JsonOptions)
            ?? throw new JsonException($"Empty {typeof(TEvent).Name} message");
}
