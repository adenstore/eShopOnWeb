using System.Threading;
using Microsoft.eShopWeb.CatalogService.Contracts;
using Microsoft.eShopWeb.Web.Interfaces;

namespace Microsoft.eShopWeb.FunctionalTests.Web;

/// <summary>
/// Stands in for the Catalog microservice so the Web app's functional tests
/// don't need the service running. Holds the same seed data as CatalogService.
/// </summary>
public class FakeCatalogApiClient : ICatalogApiClient
{
    private static readonly List<CatalogBrandDto> Brands = new()
    {
        new() { Id = 1, Name = "Azure" },
        new() { Id = 2, Name = ".NET" },
        new() { Id = 3, Name = "Visual Studio" },
        new() { Id = 4, Name = "SQL Server" },
        new() { Id = 5, Name = "Other" }
    };

    private static readonly List<CatalogTypeDto> Types = new()
    {
        new() { Id = 1, Name = "Mug" },
        new() { Id = 2, Name = "T-Shirt" },
        new() { Id = 3, Name = "Sheet" },
        new() { Id = 4, Name = "USB Memory Stick" }
    };

    private static readonly List<CatalogItemDto> Items = new()
    {
        Item(1, 2, 2, ".NET Bot Black Sweatshirt", 19.5M),
        Item(2, 1, 2, ".NET Black & White Mug", 8.50M),
        Item(3, 2, 5, "Prism White T-Shirt", 12),
        Item(4, 2, 2, ".NET Foundation Sweatshirt", 12),
        Item(5, 3, 5, "Roslyn Red Sheet", 8.5M),
        Item(6, 2, 2, ".NET Blue Sweatshirt", 12),
        Item(7, 2, 5, "Roslyn Red T-Shirt", 12),
        Item(8, 2, 5, "Kudu Purple Sweatshirt", 8.5M),
        Item(9, 1, 5, "Cup<T> White Mug", 12),
        Item(10, 3, 2, ".NET Foundation Sheet", 12),
        Item(11, 3, 2, "Cup<T> Sheet", 8.5M),
        Item(12, 2, 5, "Prism White TShirt", 12)
    };

    public Task<CatalogItemsPage> ListCatalogItemsAsync(int pageIndex, int pageSize, int? brandId, int? typeId, CancellationToken cancellationToken = default)
    {
        var filtered = Items
            .Where(i => (!brandId.HasValue || i.CatalogBrandId == brandId) && (!typeId.HasValue || i.CatalogTypeId == typeId))
            .ToList();
        var page = filtered.Skip(pageIndex * pageSize).Take(pageSize == 0 ? int.MaxValue : pageSize).ToList();
        return Task.FromResult(new CatalogItemsPage(page, filtered.Count));
    }

    public Task<IReadOnlyList<CatalogBrandDto>> ListBrandsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<CatalogBrandDto>>(Brands);
    }

    public Task<IReadOnlyList<CatalogTypeDto>> ListTypesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<CatalogTypeDto>>(Types);
    }

    private static CatalogItemDto Item(int id, int typeId, int brandId, string name, decimal price) => new()
    {
        Id = id,
        CatalogTypeId = typeId,
        CatalogBrandId = brandId,
        Name = name,
        Description = name,
        Price = price,
        PictureUri = $"/images/products/{id}.png"
    };
}
