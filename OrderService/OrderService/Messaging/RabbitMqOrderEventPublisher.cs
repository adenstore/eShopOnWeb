using System.Text.Json;
using Microsoft.eShopWeb.OrderService.Contracts.Events;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Microsoft.eShopWeb.OrderService.Messaging;

/// <summary>
/// Publishes order events to the "order-events" fanout exchange (publish/subscribe).
/// One connection is shared for the whole service; each message gets its own short-lived channel.
/// </summary>
public sealed class RabbitMqOrderEventPublisher : IOrderEventPublisher, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly RabbitMqSettings _settings;
    private readonly ILogger<RabbitMqOrderEventPublisher> _logger;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private IConnection? _connection;

    public RabbitMqOrderEventPublisher(IOptions<RabbitMqSettings> settings, ILogger<RabbitMqOrderEventPublisher> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task PublishAsync(OrderCreatedIntegrationEvent orderCreated, CancellationToken cancellationToken = default)
    {
        const string eventName = nameof(OrderCreatedIntegrationEvent);
        try
        {
            var connection = await GetConnectionAsync(cancellationToken);
            await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

            // Declaring is idempotent: it creates the exchange the first time and is a no-op afterwards.
            await channel.ExchangeDeclareAsync(OrderEventsExchange.Name, ExchangeType.Fanout, durable: true, cancellationToken: cancellationToken);

            var properties = new BasicProperties
            {
                MessageId = orderCreated.EventId.ToString(),
                ContentType = "application/json",
                Type = eventName,
                Persistent = true
            };
            var body = JsonSerializer.SerializeToUtf8Bytes(orderCreated, JsonOptions);

            await channel.BasicPublishAsync(OrderEventsExchange.Name, routingKey: string.Empty, mandatory: false,
                basicProperties: properties, body: body, cancellationToken: cancellationToken);

            _logger.LogInformation("Published {EventName} for order {OrderId} to {Exchange}",
                eventName, orderCreated.OrderId, OrderEventsExchange.Name);
        }
        catch (Exception ex)
        {
            // The order is already saved at this point, so the request should not fail because
            // RabbitMQ is unavailable. The event is lost in that case; an outbox table would prevent that.
            _logger.LogError(ex, "Could not publish {EventName} for order {OrderId} to RabbitMQ at {HostName}:{Port}",
                eventName, orderCreated.OrderId, _settings.HostName, _settings.Port);
        }
    }

    private async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true })
        {
            return _connection;
        }

        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (_connection is not { IsOpen: true })
            {
                var factory = new ConnectionFactory
                {
                    HostName = _settings.HostName,
                    Port = _settings.Port,
                    UserName = _settings.UserName,
                    Password = _settings.Password,
                    ClientProvidedName = "OrderService"
                };
                _connection = await factory.CreateConnectionAsync(cancellationToken);
            }
            return _connection;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.CloseAsync();
            await _connection.DisposeAsync();
        }
        _connectionLock.Dispose();
    }
}
