using Ardalis.Result;
using Microsoft.eShopWeb.BasketService.Domain;

namespace Microsoft.eShopWeb.BasketService.Interfaces;

public interface IBasketService
{
    Task TransferBasketAsync(string anonymousId, string userName);
    Task<Basket> GetOrCreateBasketAsync(string buyerId);

    /// <summary>
    /// Looks a buyer's basket up without creating one. Needed by the read path and by the
    /// OrderCreated handler: both have to be able to answer "there is no basket", which
    /// <see cref="GetOrCreateBasketAsync"/> cannot do.
    /// </summary>
    Task<Basket?> GetBasketByBuyerAsync(string buyerId);

    Task<Basket?> GetBasketAsync(int basketId);
    Task<Basket> AddItemToBasket(string username, int catalogItemId, decimal price, int quantity = 1);
    Task<Result<Basket>> SetQuantities(int basketId, Dictionary<string, int> quantities);
    Task DeleteBasketAsync(int basketId);
}
