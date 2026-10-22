using Microsoft.eShopWeb.BasketService.Domain;

namespace Microsoft.eShopWeb.BasketService.Dtos;

/// <summary>
/// A basket as it crosses the service boundary. Unreferenced since the REST controller was
/// removed — kept because <see cref="FromBasket"/> is the mapping the BasketChanged event
/// will need, and these are the shapes the message contracts want.
/// <para>
/// Carries no catalog data beyond <see cref="BasketItemDto.CatalogItemId"/>, which is what
/// lets this service keep a database of its own. Note that the planned design revisits this:
/// denormalising product name and picture onto the event is what would let the Web layer stop
/// reading catalog tables directly. That trade is not made here.
/// </para>
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
