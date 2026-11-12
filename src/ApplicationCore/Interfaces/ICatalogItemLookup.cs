using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The monolith's view of the Catalog microservice, for code in ApplicationCore that needs product details
/// (currently checkout, in <see cref="Services.OrderCheckoutService"/>). Implemented over HTTP in the Web
/// project, because ApplicationCore cannot reference Web. Replaces reading CatalogItem from the monolith's CatalogDb.
/// </summary>
public interface ICatalogItemLookup
{
    /// <summary>
    /// Gets the catalog items with the given ids. Ids that CatalogService does not know are left out of the result.
    /// </summary>
    Task<IReadOnlyList<CatalogItemDetails>> GetCatalogItemsAsync(IEnumerable<int> catalogItemIds, CancellationToken cancellationToken = default);
}

/// <summary>
/// The product details checkout needs. PictureUri is already a complete URL (CatalogService composes it).
/// </summary>
public record CatalogItemDetails(int Id, string Name, string PictureUri);
