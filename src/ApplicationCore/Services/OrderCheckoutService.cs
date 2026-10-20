using System.Linq;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BasketAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.OrderService.Contracts;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Turns a basket into an order request and hands it to the Order microservice.
/// Basket and catalog still live in the monolith, so the item details are
/// snapshotted here before they are sent.
/// </summary>
public class OrderCheckoutService : IOrderService
{
    private readonly IOrderApiClient _orderApiClient;
    private readonly IUriComposer _uriComposer;
    private readonly IRepository<Basket> _basketRepository;
    private readonly IRepository<CatalogItem> _itemRepository;

    public OrderCheckoutService(IRepository<Basket> basketRepository,
        IRepository<CatalogItem> itemRepository,
        IOrderApiClient orderApiClient,
        IUriComposer uriComposer)
    {
        _orderApiClient = orderApiClient;
        _uriComposer = uriComposer;
        _basketRepository = basketRepository;
        _itemRepository = itemRepository;
    }

    public async Task<int> CreateOrderAsync(int basketId, AddressDto shippingAddress)
    {
        var basketSpec = new BasketWithItemsSpecification(basketId);
        var basket = await _basketRepository.FirstOrDefaultAsync(basketSpec);

        Guard.Against.Null(basket, nameof(basket));
        Guard.Against.EmptyBasketOnCheckout(basket.Items);

        var catalogItemsSpecification = new CatalogItemsSpecification(basket.Items.Select(item => item.CatalogItemId).ToArray());
        var catalogItems = await _itemRepository.ListAsync(catalogItemsSpecification);

        var items = basket.Items.Select(basketItem =>
        {
            var catalogItem = catalogItems.First(c => c.Id == basketItem.CatalogItemId);
            return new CreateOrderItemDto(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri), basketItem.UnitPrice, basketItem.Quantity);
        }).ToList();

        return await _orderApiClient.CreateOrderAsync(new CreateOrderRequest(basket.BuyerId, shippingAddress, items));
    }
}
