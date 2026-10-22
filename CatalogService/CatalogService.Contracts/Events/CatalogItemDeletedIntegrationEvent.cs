namespace Microsoft.eShopWeb.CatalogService.Contracts.Events;

/// <summary>
/// Published by the Catalog microservice after a catalog item has been deleted.
/// </summary>
public record CatalogItemDeletedIntegrationEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    int CatalogItemId);
