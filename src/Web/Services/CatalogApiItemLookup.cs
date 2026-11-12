using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Web.Interfaces;

namespace Microsoft.eShopWeb.Web.Services;

/// <summary>
/// Implements ApplicationCore's <see cref="ICatalogItemLookup"/> on top of <see cref="ICatalogApiClient"/>,
/// so checkout (OrderCheckoutService) reads product details from CatalogService instead of the monolith's CatalogDb.
/// CatalogService has no "get many by id" endpoint, so each distinct item is fetched by id, in parallel.
/// </summary>
public class CatalogApiItemLookup : ICatalogItemLookup
{
    private readonly ICatalogApiClient _catalogApiClient;

    public CatalogApiItemLookup(ICatalogApiClient catalogApiClient)
    {
        _catalogApiClient = catalogApiClient;
    }

    public async Task<IReadOnlyList<CatalogItemDetails>> GetCatalogItemsAsync(IEnumerable<int> catalogItemIds, CancellationToken cancellationToken = default)
    {
        var fetchedItems = await Task.WhenAll(catalogItemIds.Distinct()
            .Select(id => _catalogApiClient.GetCatalogItemAsync(id, cancellationToken)));

        return fetchedItems
            .Where(item => item != null)
            .Select(item => new CatalogItemDetails(item!.Id, item.Name, item.PictureUri))
            .ToList();
    }
}
