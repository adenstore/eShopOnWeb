using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Web.Interfaces;
using Microsoft.eShopWeb.Web.ViewModels;

namespace Microsoft.eShopWeb.Web.Pages.Basket;

public class IndexModel : PageModel
{
    private readonly IBasketService _basketService;
    private readonly IBasketViewModelService _basketViewModelService;
    // Commented out on purpose: the item (and its price) used to be read from the monolith's CatalogDb through
    // IRepository<CatalogItem>. It is now read from CatalogService through ICatalogApiClient, so the monolith's
    // catalog code can be removed. Old lines are kept here for reference.
    //private readonly IRepository<CatalogItem> _itemRepository;
    private readonly ICatalogApiClient _catalogApiClient;

    //public IndexModel(IBasketService basketService,
    //    IBasketViewModelService basketViewModelService,
    //    IRepository<CatalogItem> itemRepository)
    //{
    //    _basketService = basketService;
    //    _basketViewModelService = basketViewModelService;
    //    _itemRepository = itemRepository;
    //}
    public IndexModel(IBasketService basketService,
        IBasketViewModelService basketViewModelService,
        ICatalogApiClient catalogApiClient)
    {
        _basketService = basketService;
        _basketViewModelService = basketViewModelService;
        _catalogApiClient = catalogApiClient;
    }

    public BasketViewModel BasketModel { get; set; } = new BasketViewModel();

    public async Task OnGet()
    {
        BasketModel = await _basketViewModelService.GetBasketForUser(GetOrSetBasketCookieAndUserName());
    }

    public async Task<IActionResult> OnPost(CatalogItemViewModel productDetails)
    {
        if (productDetails?.Id == null)
        {
            return RedirectToPage("/Index");
        }

        // Commented out on purpose: reads the price from CatalogService instead of the monolith's CatalogDb (see above).
        //var item = await _itemRepository.GetByIdAsync(productDetails.Id);
        var item = await _catalogApiClient.GetCatalogItemAsync(productDetails.Id);
        if (item == null)
        {
            return RedirectToPage("/Index");
        }

        var username = GetOrSetBasketCookieAndUserName();
        await _basketService.AddItemToBasket(username, productDetails.Id, item.Price);

        // Nothing is rendered here: this is Post-Redirect-Get, and the redirect re-reads the
        // basket. The gateway serves that read from BasketService rather than from its projection
        // while this command is still in flight, so the item shows up on the first render.
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdate(IEnumerable<BasketItemViewModel> items)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var basketView = await _basketViewModelService.GetBasketForUser(GetOrSetBasketCookieAndUserName());
        if (!basketView.HasBasket)
        {
            // Nothing to update. Sending the command anyway would address basket 0, which the
            // gateway rejects with a 400, and EnsureSuccessStatusCode would turn that into a 500.
            return RedirectToPage();
        }

        var updateModel = items.ToDictionary(b => b.Id.ToString(), b => b.Quantity);
        await _basketService.SetQuantities(basketView.Id, updateModel);

        // Redirect rather than render: SetQuantities is a one-way command with no updated basket
        // to show. It also stops a refresh of the basket page from re-submitting the form.
        return RedirectToPage();
    }

    private string GetOrSetBasketCookieAndUserName()
    {
        Guard.Against.Null(Request.HttpContext.User.Identity, nameof(Request.HttpContext.User.Identity));
        string? userName = null;

        if (Request.HttpContext.User.Identity.IsAuthenticated)
        {
            Guard.Against.Null(Request.HttpContext.User.Identity.Name, nameof(Request.HttpContext.User.Identity.Name));
            return Request.HttpContext.User.Identity.Name!;
        }

        if (Request.Cookies.ContainsKey(Constants.BASKET_COOKIENAME))
        {
            userName = Request.Cookies[Constants.BASKET_COOKIENAME];

            if (!Request.HttpContext.User.Identity.IsAuthenticated)
            {
                if (!Guid.TryParse(userName, out var _))
                {
                    userName = null;
                }
            }
        }
        if (userName != null) return userName;

        userName = Guid.NewGuid().ToString();
        var cookieOptions = new CookieOptions { IsEssential = true };
        cookieOptions.Expires = DateTime.Today.AddYears(10);
        Response.Cookies.Append(Constants.BASKET_COOKIENAME, userName, cookieOptions);

        return userName;
    }
}
