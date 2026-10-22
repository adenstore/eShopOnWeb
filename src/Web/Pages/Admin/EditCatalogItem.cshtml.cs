// ------------------------------------------------------------------------------------------------
// COMMENTED OUT ON PURPOSE - Catalog microservice migration (CatalogService/)
//
// What it is: an old Razor admin page that lets an administrator change a catalog item's name and
// price. It writes straight to the monolith's own catalog database (CatalogDb) through
// CatalogItemViewModelService, which uses the monolith's CatalogItem entity.
//
// Why it is off:
// - It is unreachable. Nothing links to it (the only form that did, Pages/Shared/_editCatalog.cshtml,
//   is not used anywhere), and the admin menu opens BlazorAdmin instead.
// - BlazorAdmin is the real admin UI. It edits the catalog through the ApiGateway in CatalogService,
//   so nothing is lost by switching this page off.
// - It depends on the monolith's catalog entity (CatalogItem). When the monolith's catalog code is
//   removed in the final step of the migration, this page would stop compiling.
// - Admin/front-end stays in the monolith by team decision and is not part of the catalog extraction,
//   so the page is kept here (commented out) instead of being moved or deleted.
//
// To bring it back, uncomment all four pieces together:
//   Pages/Admin/EditCatalogItem.cshtml.cs   (this file)
//   Pages/Admin/EditCatalogItem.cshtml
//   Services/CatalogItemViewModelService.cs
//   the CatalogItemViewModelService registration in Configuration/ConfigureWebServices.cs
// and change CatalogItemViewModelService to call CatalogService instead of the monolith's CatalogDb.
// ------------------------------------------------------------------------------------------------

//using System.Threading.Tasks;
//using Microsoft.AspNetCore.Authorization;
//using Microsoft.AspNetCore.Mvc;
//using Microsoft.AspNetCore.Mvc.RazorPages;
//using Microsoft.eShopWeb.ApplicationCore.Constants;
//using Microsoft.eShopWeb.Web.Interfaces;
//using Microsoft.eShopWeb.Web.ViewModels;

//namespace Microsoft.eShopWeb.Web.Pages.Admin;

//[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS)]
//public class EditCatalogItemModel : PageModel
//{
//    private readonly ICatalogItemViewModelService _catalogItemViewModelService;

//    public EditCatalogItemModel(ICatalogItemViewModelService catalogItemViewModelService)
//    {
//        _catalogItemViewModelService = catalogItemViewModelService;
//    }

//    [BindProperty]
//    public CatalogItemViewModel CatalogModel { get; set; } = new CatalogItemViewModel();

//    public void OnGet(CatalogItemViewModel catalogModel)
//    {
//        CatalogModel = catalogModel;
//    }

//    public async Task<IActionResult> OnPostAsync()
//    {
//        if (ModelState.IsValid)
//        {
//            await _catalogItemViewModelService.UpdateCatalogItem(CatalogModel);
//        }

//        return RedirectToPage("/Admin/Index");
//    }
//}
