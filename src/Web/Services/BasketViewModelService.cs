using Microsoft.eShopWeb.ApplicationCore.Dtos;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Web.Interfaces;
using Microsoft.eShopWeb.Web.Pages.Basket;

namespace Microsoft.eShopWeb.Web.Services;

public class BasketViewModelService : IBasketViewModelService
{
    private readonly IBasketService _basketService;
    // Commented out on purpose: IUriComposer was only used to turn the monolith's picture paths into URLs.
    // CatalogService returns complete picture URLs, and IUriComposer is removed from the monolith together
    // with the rest of its catalog code.
    //private readonly IUriComposer _uriComposer;
    private readonly IBasketQueryService _basketQueryService;
    // Commented out on purpose: product name and picture for each basket line used to be read from the
    // monolith's CatalogDb through IRepository<CatalogItem>. They are now read from CatalogService through
    // ICatalogApiClient, so the monolith's catalog code can be removed. Old lines are kept here for reference.
    //private readonly IRepository<CatalogItem> _itemRepository;
    private readonly ICatalogApiClient _catalogApiClient;

    //public BasketViewModelService(IBasketService basketService,
    //    IRepository<CatalogItem> itemRepository,
    //    IUriComposer uriComposer,
    //    IBasketQueryService basketQueryService)
    //{
    //    _basketService = basketService;
    //    _uriComposer = uriComposer;
    //    _basketQueryService = basketQueryService;
    //    _itemRepository = itemRepository;
    //}
    //public BasketViewModelService(IBasketService basketService,
    //    ICatalogApiClient catalogApiClient,
    //    IUriComposer uriComposer,
    //    IBasketQueryService basketQueryService)
    //{
    //    _basketService = basketService;
    //    _uriComposer = uriComposer;
    //    _basketQueryService = basketQueryService;
    //    _catalogApiClient = catalogApiClient;
    //}
    public BasketViewModelService(IBasketService basketService,
        ICatalogApiClient catalogApiClient,
        IBasketQueryService basketQueryService)
    {
        _basketService = basketService;
        _basketQueryService = basketQueryService;
        _catalogApiClient = catalogApiClient;
    }

    public async Task<BasketViewModel> GetOrCreateBasketForUser(string userName)
    {
        var basket = await _basketService.GetOrCreateBasketAsync(userName);

        return await Map(basket);
    }

    private async Task<List<BasketItemViewModel>> GetBasketItems(IReadOnlyCollection<BasketItemDto> basketItems)
    {
        // Commented out on purpose: the catalog items are fetched from CatalogService instead of the monolith's
        // CatalogDb (see the constructor). CatalogService has no "get many by id" endpoint, so each distinct
        // item is fetched by id; the requests run in parallel, and a basket only holds a handful of items.
        //var catalogItemsSpecification = new CatalogItemsSpecification(basketItems.Select(b => b.CatalogItemId).ToArray());
        //var catalogItems = await _itemRepository.ListAsync(catalogItemsSpecification);
        var catalogItemIds = basketItems.Select(b => b.CatalogItemId).Distinct();
        var fetchedItems = await Task.WhenAll(catalogItemIds.Select(id => _catalogApiClient.GetCatalogItemAsync(id)));
        var catalogItems = fetchedItems.Where(c => c != null).Select(c => c!).ToList();

        var items = basketItems.Select(basketItem =>
        {
            var catalogItem = catalogItems.First(c => c.Id == basketItem.CatalogItemId);

            var basketItemViewModel = new BasketItemViewModel
            {
                Id = basketItem.Id,
                UnitPrice = basketItem.UnitPrice,
                Quantity = basketItem.Quantity,
                CatalogItemId = basketItem.CatalogItemId,
                // Commented out on purpose: CatalogService already returns a complete picture URL,
                // so it must not be run through ComposePicUri a second time.
                //PictureUrl = _uriComposer.ComposePicUri(catalogItem.PictureUri),
                PictureUrl = catalogItem.PictureUri,
                ProductName = catalogItem.Name
            };
            return basketItemViewModel;
        }).ToList();

        return items;
    }

    public async Task<BasketViewModel> Map(BasketDto basket)
    {
        return new BasketViewModel()
        {
            BuyerId = basket.BuyerId,
            Id = basket.Id,
            Items = await GetBasketItems(basket.Items)
        };
    }

    public async Task<int> CountTotalBasketItems(string username)
    {
        var counter = await _basketQueryService.CountTotalBasketItems(username);

        return counter;
    }
}
