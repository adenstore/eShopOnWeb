namespace Microsoft.eShopWeb.CatalogService.Contracts.Events;

/// <summary>
/// Published by the Catalog microservice after a catalog item has been changed.
/// Carries the item's full current state, so subscribers can simply overwrite their copy.
/// </summary>
public record CatalogItemUpdatedIntegrationEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    int CatalogItemId,
    string Name,
    string Description,
    decimal Price,
    string PictureUri,
    int CatalogBrandId,
    int CatalogTypeId);
