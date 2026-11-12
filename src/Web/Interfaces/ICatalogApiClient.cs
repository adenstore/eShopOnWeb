using Microsoft.eShopWeb.CatalogService.Contracts;

namespace Microsoft.eShopWeb.Web.Interfaces;

/// <summary>
/// The Web app's view of the Catalog microservice. Implemented over HTTP in <see cref="Services.CatalogApiClient"/>.
/// </summary>
public interface ICatalogApiClient
{
    Task<CatalogItemsPage> ListCatalogItemsAsync(int pageIndex, int pageSize, int? brandId, int? typeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CatalogBrandDto>> ListBrandsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CatalogTypeDto>> ListTypesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a single catalog item, or null if CatalogService has no item with that id.
    /// Used by the basket pages, which used to read the item from the monolith's CatalogDb.
    /// </summary>
    Task<CatalogItemDto?> GetCatalogItemAsync(int catalogItemId, CancellationToken cancellationToken = default);
}

public record CatalogItemsPage(IReadOnlyList<CatalogItemDto> Items, int TotalItems);
