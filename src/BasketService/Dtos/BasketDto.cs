using Microsoft.eShopWeb.BasketService.Domain;

namespace Microsoft.eShopWeb.BasketService.Dtos;

/// <summary>
/// A basket as it crosses the service boundary.
/// Deliberately carries no catalog data beyond <see cref="BasketItemDto.CatalogItemId"/> —
/// resolving product names and pictures stays with whoever owns the catalog, which is
/// what lets this service keep a database of its own.
/// </summary>
public class BasketDto
{
    public int Id { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public List<BasketItemDto> Items { get; set; } = new();

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

public record BasketItemCountResponse(int Count);
