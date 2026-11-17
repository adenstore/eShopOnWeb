namespace Microsoft.eShopWeb.Web.Pages.Basket;

public class BasketViewModel
{
    /// <summary>
    /// The id a buyer with no basket is reported under. Mirrors <c>IBasketService.NoBasketId</c>,
    /// which is what the gateway answers with: baskets are created on the first add, so this is
    /// the normal state for anyone who is only browsing.
    /// </summary>
    public const int NoBasketId = 0;

    public int Id { get; set; }
    public List<BasketItemViewModel> Items { get; set; } = new List<BasketItemViewModel>();
    public string? BuyerId { get; set; }

    /// <summary>
    /// True when there is a real basket to send a command about. <see cref="NoBasketId"/> is not
    /// an id the gateway accepts — it answers 400 — so a handler that is about to post quantities
    /// or check out has to ask this first.
    /// </summary>
    public bool HasBasket => Id != NoBasketId;

    public decimal Total()
    {
        return Math.Round(Items.Sum(x => x.UnitPrice * x.Quantity), 2);
    }
}
