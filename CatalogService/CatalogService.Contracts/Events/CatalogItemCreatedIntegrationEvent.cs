namespace Microsoft.eShopWeb.CatalogService.Contracts.Events;

/// <summary>
/// Published by the Catalog microservice after a new catalog item has been saved.
/// </summary>
public record CatalogItemCreatedIntegrationEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    int CatalogItemId,
    string Name,
    string Description,
    decimal Price,
    string PictureUri,
    int CatalogBrandId,
    int CatalogTypeId);
