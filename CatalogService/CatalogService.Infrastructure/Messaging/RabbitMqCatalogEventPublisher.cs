using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.CatalogService.Contracts.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Microsoft.eShopWeb.CatalogService.Infrastructure.Messaging;

/// <summary>
/// Publishes catalog events to the "catalog-events" fanout exchange (publish/subscribe).
/// One connection is shared for the whole service; each message gets its own short-lived channel.
/// </summary>
public sealed class RabbitMqCatalogEventPublisher : ICatalogEventPublisher, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly RabbitMqSettings _settings;
    private readonly ILogger<RabbitMqCatalogEventPublisher> _logger;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private IConnection? _connection;

    public RabbitMqCatalogEventPublisher(IOptions<RabbitMqSettings> settings, ILogger<RabbitMqCatalogEventPublisher> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default) where TEvent : class
    {
        var eventName = typeof(TEvent).Name;
        try
        {
            var connection = await GetConnectionAsync(cancellationToken);
            await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

            // Declaring is idempotent: it creates the exchange the first time and is a no-op afterwards.
            await channel.ExchangeDeclareAsync(CatalogEventsExchange.Name, ExchangeType.Fanout, durable: true, cancellationToken: cancellationToken);

            var properties = new BasicProperties
            {
                ContentType = "application/json",
                Type = eventName,
                Persistent = true
            };
            var body = JsonSerializer.SerializeToUtf8Bytes(integrationEvent, JsonOptions);

            await channel.BasicPublishAsync(CatalogEventsExchange.Name, routingKey: string.Empty, mandatory: false,
                basicProperties: properties, body: body, cancellationToken: cancellationToken);

            _logger.LogInformation("Published {EventName} to {Exchange}", eventName, CatalogEventsExchange.Name);
        }
        catch (Exception ex)
        {
            // The database change is already saved at this point, so the request should not fail because
            // RabbitMQ is unavailable. The event is lost in that case; an outbox table would prevent that.
            _logger.LogError(ex, "Could not publish {EventName} to RabbitMQ at {HostName}:{Port}", eventName, _settings.HostName, _settings.Port);
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
                    ClientProvidedName = "CatalogService"
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
