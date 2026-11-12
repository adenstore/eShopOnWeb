using RabbitMQ.Client;

namespace Microsoft.eShopWeb.BasketService.Messaging;

/// <summary>
/// Shared startup behaviour for the queue consumers: keep trying to attach to the broker rather
/// than taking the process down.
/// <para>
/// This exists because an exception out of <see cref="BackgroundService.ExecuteAsync"/> stops the
/// whole host by default. A broker that was not up yet killed the entire service on startup —
/// including <c>/health</c>, so nothing could even report why. `depends_on` in compose waits for
/// the container to start, not for RabbitMQ to finish booting, so that race is routine rather than
/// exotic.
/// </para>
/// <para>
/// Only the *initial* attach is retried here. Once attached, RabbitMQ.Client recovers a dropped
/// connection, its channels, the topology and the consumers by itself
/// (<c>AutomaticRecoveryEnabled</c> and <c>TopologyRecoveryEnabled</c> are on by default), so
/// re-running the setup on every blip would duplicate consumers rather than help.
/// </para>
/// </summary>
public abstract class RabbitMqConsumerService : BackgroundService
{
    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);

    private readonly IRabbitMqConnection _connection;
    private readonly ILogger _logger;
    private IChannel? _channel;

    protected RabbitMqConsumerService(IRabbitMqConnection connection, ILogger logger)
    {
        _connection = connection;
        _logger = logger;
    }

    /// <summary>The channel this consumer is attached on. Only valid once <see cref="ConfigureAsync"/> has run.</summary>
    protected IChannel Channel => _channel
        ?? throw new InvalidOperationException("The consumer is not attached to RabbitMQ yet.");

    protected IRabbitMqConnection Connection => _connection;

    /// <summary>Declare whatever topology this consumer owns and start consuming.</summary>
    protected abstract Task ConfigureAsync(IChannel channel, CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = FirstRetryDelay;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // A previous attempt may have left a dead channel behind.
                await DisposeChannelAsync();

                var connection = await _connection.GetAsync(stoppingToken);
                _channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
                await ConfigureAsync(_channel, stoppingToken);

                delay = FirstRetryDelay;

                // Attached. The consumer callbacks drive everything from here.
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not attach {Consumer} to RabbitMQ; retrying in {Delay}s",
                    GetType().Name, delay.TotalSeconds);

                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                // Back off, so a broker that is down for a while is not hammered.
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxRetryDelay.Ticks));
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await DisposeChannelAsync();
    }

    private async Task DisposeChannelAsync()
    {
        if (_channel is null)
        {
            return;
        }

        try
        {
            if (_channel.IsOpen)
            {
                await _channel.CloseAsync();
            }
            await _channel.DisposeAsync();
        }
        catch (Exception ex)
        {
            // Closing a channel that is already gone is not a problem worth retrying over.
            _logger.LogDebug(ex, "Ignoring error while closing the previous channel");
        }
        finally
        {
            _channel = null;
        }
    }
}
