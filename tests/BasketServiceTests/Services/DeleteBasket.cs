using Microsoft.eShopWeb.BasketService.Domain;
using Microsoft.eShopWeb.BasketService.Interfaces;
using NSubstitute;
using Xunit;

using BasketSvc = Microsoft.eShopWeb.BasketService.Services.BasketService;

namespace Microsoft.eShopWeb.BasketServiceTests.Services;

public class DeleteBasket
{
    private readonly string _buyerId = "Test buyerId";
    private readonly IRepository<Basket> _mockBasketRepo = Substitute.For<IRepository<Basket>>();
    private readonly IAppLogger<BasketSvc> _mockLogger = Substitute.For<IAppLogger<BasketSvc>>();

    [Fact]
    public async Task ShouldInvokeBasketRepositoryDeleteAsyncOnce()
    {
        var basket = new Basket(_buyerId);
        basket.AddItem(1, 1.1m, 1);
        basket.AddItem(2, 1.1m, 1);

        _mockBasketRepo.GetByIdAsync(Arg.Any<int>(), default).Returns(basket);

        var basketService = new BasketSvc(_mockBasketRepo, _mockLogger);

        await basketService.DeleteBasketAsync(1);

        await _mockBasketRepo.Received().DeleteAsync(Arg.Any<Basket>(), default);
    }
}
