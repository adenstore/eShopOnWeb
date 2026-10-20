using Microsoft.eShopWeb.BasketService.Domain;
using Microsoft.eShopWeb.BasketService.Interfaces;
using Microsoft.eShopWeb.BasketService.Specifications;
using NSubstitute;
using Xunit;

using BasketSvc = Microsoft.eShopWeb.BasketService.Services.BasketService;

namespace Microsoft.eShopWeb.BasketServiceTests.Services;

public class AddItemToBasket
{
    private readonly string _buyerId = "Test buyerId";
    private readonly IRepository<Basket> _mockBasketRepo = Substitute.For<IRepository<Basket>>();
    private readonly IAppLogger<BasketSvc> _mockLogger = Substitute.For<IAppLogger<BasketSvc>>();

    [Fact]
    public async Task InvokesBasketRepositoryGetBySpecAsyncOnce()
    {
        var basket = new Basket(_buyerId);
        basket.AddItem(1, 1.5m);

        _mockBasketRepo.FirstOrDefaultAsync(Arg.Any<BasketWithItemsSpecification>(), default).Returns(basket);

        var basketService = new BasketSvc(_mockBasketRepo, _mockLogger);

        await basketService.AddItemToBasket(basket.BuyerId, 1, 1.50m);

        await _mockBasketRepo.Received().FirstOrDefaultAsync(Arg.Any<BasketWithItemsSpecification>(), default);
    }

    [Fact]
    public async Task InvokesBasketRepositoryUpdateAsyncOnce()
    {
        var basket = new Basket(_buyerId);
        basket.AddItem(1, 1.1m, 1);

        _mockBasketRepo.FirstOrDefaultAsync(Arg.Any<BasketWithItemsSpecification>(), default).Returns(basket);

        var basketService = new BasketSvc(_mockBasketRepo, _mockLogger);

        await basketService.AddItemToBasket(basket.BuyerId, 1, 1.50m);

        await _mockBasketRepo.Received().UpdateAsync(basket, default);
    }
}
