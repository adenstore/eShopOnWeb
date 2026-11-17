using Microsoft.eShopWeb.Web.Pages.Basket;

namespace Microsoft.eShopWeb.Web.Interfaces;

public interface IBasketViewModelService
{
    /// <summary>
    /// The buyer's basket, or an empty one at <see cref="BasketViewModel.NoBasketId"/> when they
    /// have none — reading does not create one. Check <see cref="BasketViewModel.HasBasket"/>
    /// before using the id to address a command.
    /// </summary>
    Task<BasketViewModel> GetBasketForUser(string userName);
    Task<int> CountTotalBasketItems(string username);

    // Map is gone from the interface: it only existed so the pages could render the basket a write
    // returned, and the writes are one-way commands now. The pages redirect and read instead.
}
