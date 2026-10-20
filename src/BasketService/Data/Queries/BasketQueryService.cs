using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.BasketService.Interfaces;

namespace Microsoft.eShopWeb.BasketService.Data.Queries;

public class BasketQueryService : IBasketQueryService
{
    private readonly BasketDbContext _dbContext;

    public BasketQueryService(BasketDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// This method performs the sum on the database rather than in memory
    /// </summary>
    /// <param name="username"></param>
    /// <returns></returns>
    public async Task<int> CountTotalBasketItems(string username)
    {
        var totalItems = await _dbContext.Baskets
            .Where(basket => basket.BuyerId == username)
            .SelectMany(item => item.Items)
            .SumAsync(sum => sum.Quantity);

        return totalItems;
    }
}
