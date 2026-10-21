using System.Collections.Generic;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Dtos;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IBasketService
{
    Task TransferBasketAsync(string anonymousId, string userName);
    Task<BasketDto> GetOrCreateBasketAsync(string buyerId);
    Task<BasketDto?> GetBasketAsync(int basketId);
    Task<BasketDto> AddItemToBasket(string username, int catalogItemId, decimal price, int quantity = 1);
    Task<Result<BasketDto>> SetQuantities(int basketId, Dictionary<string, int> quantities);
    Task DeleteBasketAsync(int basketId);
}
