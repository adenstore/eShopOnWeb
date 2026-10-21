using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.BasketAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Dtos;

public class BasketDto
{
    public int Id { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public List<BasketItemDto> Items { get; set; } = new();

    public int TotalItems => Items.Sum(i => i.Quantity);

    public static BasketDto FromBasket(Basket basket) => new()
    {
        Id = basket.Id,
        BuyerId = basket.BuyerId,
        Items = basket.Items.Select(BasketItemDto.FromBasketItem).ToList()
    };
}

public class BasketItemDto
{
    public int Id { get; set; }
    public int CatalogItemId { get; set; }
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }

    public static BasketItemDto FromBasketItem(BasketItem item) => new()
    {
        Id = item.Id,
        CatalogItemId = item.CatalogItemId,
        UnitPrice = item.UnitPrice,
        Quantity = item.Quantity
    };
}
