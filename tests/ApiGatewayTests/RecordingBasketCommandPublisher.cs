using System.Collections.Concurrent;
using Microsoft.eShopWeb.ApiGateway.Messaging;

namespace Microsoft.eShopWeb.ApiGateway.Tests;

/// <summary>
/// Stands in for the broker. These tests are about what the gateway does with an HTTP request —
/// which command it builds, which routing key it sends it under, and which status code it answers —
/// so the publish itself is recorded rather than performed. That messages really route through a
/// broker to BasketService is proven by BasketService.IntegrationTests against a real RabbitMQ.
/// </summary>
public sealed class RecordingBasketCommandPublisher : IBasketCommandPublisher
{
    private readonly ConcurrentQueue<(string RoutingKey, object Command)> _published = new();

    /// <summary>Set to make the next publish throw, as a broker that cannot be reached does.</summary>
    public Exception? FailWith { get; set; }

    public IReadOnlyList<(string RoutingKey, object Command)> Published => _published.ToArray();

    /// <summary>Drops what was recorded, so one host can be shared by tests that each start clean.</summary>
    public void Reset()
    {
        _published.Clear();
        FailWith = null;
    }

    public Task PublishAsync(string routingKey, object command, CancellationToken cancellationToken = default)
    {
        if (FailWith is not null)
        {
            return Task.FromException(FailWith);
        }

        _published.Enqueue((routingKey, command));
        return Task.CompletedTask;
    }
}
