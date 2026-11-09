using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Microsoft.eShopWeb.BasketService.Messaging;

/// <summary>
/// One AMQP connection shared by the whole service, opened on first use and reopened if it drops.
/// Channels are cheap and are not thread-safe, so callers take a short-lived channel per message
/// (or one long-lived channel per consumer); connections are expensive, so there is just this one.
/// </summary>
public interface IRabbitMqConnection
{
    Task<IConnection> GetAsync(CancellationToken cancellationToken = default);
}

public sealed class RabbitMqConnection : IRabbitMqConnection, IAsyncDisposable
{
    private readonly RabbitMqSettings _settings;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private IConnection? _connection;

    public RabbitMqConnection(IOptions<RabbitMqSettings> settings) => _settings = settings.Value;

    public async Task<IConnection> GetAsync(CancellationToken cancellationToken = default)
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
                    ClientProvidedName = "BasketService"
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
