// ------------------------------------------------------------------------------------------------
// COMMENTED OUT ON PURPOSE - Catalog microservice migration (CatalogService/)
//
// This service is only used by the old Razor admin page Pages/Admin/EditCatalogItem, which is
// commented out because it is unreachable and BlazorAdmin (through CatalogService) is the real admin UI.
//
// This is the file that would actually break: it updates catalog items directly in the monolith's
// CatalogDb using the monolith's CatalogItem entity and IRepository<CatalogItem>. Those are removed
// when the monolith's catalog code is cleaned up at the end of the migration.
//
// See Pages/Admin/EditCatalogItem.cshtml.cs for the full explanation and how to bring it back.
// If it is brought back, it should call CatalogService (PUT api/catalog-items) instead of CatalogDb.
// ------------------------------------------------------------------------------------------------

//using Ardalis.GuardClauses;
//using Microsoft.eShopWeb.ApplicationCore.Entities;
//using Microsoft.eShopWeb.ApplicationCore.Interfaces;
//using Microsoft.eShopWeb.Web.Interfaces;
//using Microsoft.eShopWeb.Web.ViewModels;

//namespace Microsoft.eShopWeb.Web.Services;

//public class CatalogItemViewModelService : ICatalogItemViewModelService
//{
//    private readonly IRepository<CatalogItem> _catalogItemRepository;

//    public CatalogItemViewModelService(IRepository<CatalogItem> catalogItemRepository)
//    {
//        _catalogItemRepository = catalogItemRepository;
//    }

//    public async Task UpdateCatalogItem(CatalogItemViewModel viewModel)
//    {
//        var existingCatalogItem = await _catalogItemRepository.GetByIdAsync(viewModel.Id);

//        Guard.Against.Null(existingCatalogItem, nameof(existingCatalogItem));

//        CatalogItem.CatalogItemDetails details = new(viewModel.Name, existingCatalogItem.Description, viewModel.Price);
//        existingCatalogItem.UpdateDetails(details);
//        await _catalogItemRepository.UpdateAsync(existingCatalogItem);
//    }
//}
