using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.eShopWeb.Web.Interfaces;
using Microsoft.eShopWeb.Web.ViewModels;

namespace Microsoft.eShopWeb.Web.Services;

/// <summary>
/// Builds the storefront's catalog view models from the Catalog microservice instead of the
/// monolith's own catalog database. Same output as <see cref="CatalogViewModelService"/>.
/// </summary>
public class CatalogApiViewModelService : ICatalogViewModelService
{
    private readonly ILogger<CatalogApiViewModelService> _logger;
    private readonly ICatalogApiClient _catalogApiClient;

    public CatalogApiViewModelService(ILogger<CatalogApiViewModelService> logger, ICatalogApiClient catalogApiClient)
    {
        _logger = logger;
        _catalogApiClient = catalogApiClient;
    }

    public async Task<CatalogIndexViewModel> GetCatalogItems(int pageIndex, int itemsPage, int? brandId, int? typeId)
    {
        _logger.LogInformation("GetCatalogItems called.");

        var page = await _catalogApiClient.ListCatalogItemsAsync(pageIndex, itemsPage, brandId, typeId);

        var vm = new CatalogIndexViewModel()
        {
            // CatalogService already turns PictureUri into a usable URL, so it is used as-is.
            CatalogItems = page.Items.Select(i => new CatalogItemViewModel()
            {
                Id = i.Id,
                Name = i.Name,
                PictureUri = i.PictureUri,
                Price = i.Price
            }).ToList(),
            Brands = (await GetBrands()).ToList(),
            Types = (await GetTypes()).ToList(),
            BrandFilterApplied = brandId ?? 0,
            TypesFilterApplied = typeId ?? 0,
            PaginationInfo = new PaginationInfoViewModel()
            {
                ActualPage = pageIndex,
                ItemsPerPage = page.Items.Count,
                TotalItems = page.TotalItems,
                TotalPages = int.Parse(Math.Ceiling(((decimal)page.TotalItems / itemsPage)).ToString())
            }
        };

        vm.PaginationInfo.Next = (vm.PaginationInfo.ActualPage == vm.PaginationInfo.TotalPages - 1) ? "is-disabled" : "";
        vm.PaginationInfo.Previous = (vm.PaginationInfo.ActualPage == 0) ? "is-disabled" : "";

        return vm;
    }

    public async Task<IEnumerable<SelectListItem>> GetBrands()
    {
        _logger.LogInformation("GetBrands called.");
        var brands = await _catalogApiClient.ListBrandsAsync();

        var items = brands
            .Select(brand => new SelectListItem() { Value = brand.Id.ToString(), Text = brand.Name })
            .OrderBy(b => b.Text)
            .ToList();

        var allItem = new SelectListItem() { Value = null, Text = "All", Selected = true };
        items.Insert(0, allItem);

        return items;
    }

    public async Task<IEnumerable<SelectListItem>> GetTypes()
    {
        _logger.LogInformation("GetTypes called.");
        var types = await _catalogApiClient.ListTypesAsync();

        var items = types
            .Select(type => new SelectListItem() { Value = type.Id.ToString(), Text = type.Name })
            .OrderBy(t => t.Text)
            .ToList();

        var allItem = new SelectListItem() { Value = null, Text = "All", Selected = true };
        items.Insert(0, allItem);

        return items;
    }
}
