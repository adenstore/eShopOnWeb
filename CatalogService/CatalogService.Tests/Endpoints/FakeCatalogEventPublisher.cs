using System.Collections.Concurrent;
using Microsoft.eShopWeb.CatalogService.Infrastructure.Messaging;

namespace Microsoft.eShopWeb.CatalogService.Tests.Endpoints;

/// <summary>
/// Collects published events in memory instead of sending them to RabbitMQ,
/// so tests can check what would have been published.
/// </summary>
public class FakeCatalogEventPublisher : ICatalogEventPublisher
{
    private readonly ConcurrentQueue<object> _published = new();

    public IReadOnlyCollection<object> Published => _published.ToArray();

    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default) where TEvent : class
    {
        _published.Enqueue(integrationEvent);
        return Task.CompletedTask;
    }
}
