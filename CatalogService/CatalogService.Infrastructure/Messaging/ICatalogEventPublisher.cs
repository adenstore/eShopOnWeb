using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.CatalogService.Infrastructure.Messaging;

/// <summary>
/// Publishes integration events (from CatalogService.Contracts.Events) to other services.
/// </summary>
public interface ICatalogEventPublisher
{
    Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default) where TEvent : class;
}
