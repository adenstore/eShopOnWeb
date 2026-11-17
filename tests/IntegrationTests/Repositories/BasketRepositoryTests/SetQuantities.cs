using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BasketAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Repositories.BasketRepositoryTests;

// ApplicationCore references BasketService.Contracts now, so the simple name "BasketService"
// resolves to the Microsoft.eShopWeb.BasketService namespace — an enclosing namespace member beats
// any using directive. Alias the class under test back onto its own name.
using BasketService = global::Microsoft.eShopWeb.ApplicationCore.Services.BasketService;

public class SetQuantities
{
    private readonly CatalogContext _catalogContext;
    private readonly EfRepository<Basket> _basketRepository;
    private readonly BasketBuilder BasketBuilder = new BasketBuilder();

    public SetQuantities()
    {
        var dbOptions = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(databaseName: "TestCatalog")
            .Options;
        _catalogContext = new CatalogContext(dbOptions);
        _basketRepository = new EfRepository<Basket>(_catalogContext);
    }

    [Fact]
    public async Task RemoveEmptyQuantities()
    {
        var basket = BasketBuilder.WithOneBasketItem();
        var basketService = new BasketService(_basketRepository, null);
        await _basketRepository.AddAsync(basket);
        _catalogContext.SaveChanges();

        await basketService.SetQuantities(BasketBuilder.BasketId, new Dictionary<string, int>() { { BasketBuilder.BasketId.ToString(), 0 } });

        Assert.Equal(0, basket.Items.Count);
    }
}
