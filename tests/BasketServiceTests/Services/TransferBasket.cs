using FluentAssertions;
using Microsoft.eShopWeb.BasketService.Domain;
using Microsoft.eShopWeb.BasketService.Interfaces;
using Microsoft.eShopWeb.BasketService.Specifications;
using NSubstitute;
using Xunit;

using BasketSvc = Microsoft.eShopWeb.BasketService.Services.BasketService;

namespace Microsoft.eShopWeb.BasketServiceTests.Services;

public class TransferBasket
{
    private readonly string _nonexistentAnonymousBasketBuyerId = "nonexistent-anonymous-basket-buyer-id";
    private readonly string _existentAnonymousBasketBuyerId = "existent-anonymous-basket-buyer-id";
    private readonly string _nonexistentUserBasketBuyerId = "newuser@microsoft.com";
    private readonly string _existentUserBasketBuyerId = "testuser@microsoft.com";
    private readonly IRepository<Basket> _mockBasketRepo = Substitute.For<IRepository<Basket>>();
    private readonly IAppLogger<BasketSvc> _mockLogger = Substitute.For<IAppLogger<BasketSvc>>();

    /// <summary>
    /// TransferBasketAsync queries the repository twice with the same specification type, so the
    /// substitute has to answer the two calls differently.
    /// </summary>
    private class Results<T>
    {
        private readonly Queue<Func<T>> _values = new Queue<Func<T>>();

        public Results(T result) { _values.Enqueue(() => result); }

        public Results<T> Then(T value) { return Then(() => value); }

        public Results<T> Then(Func<T> value)
        {
            _values.Enqueue(value);
            return this;
        }

        public T Next() { return _values.Dequeue()(); }
    }

    [Fact]
    public async Task InvokesBasketRepositoryFirstOrDefaultAsyncOnceIfAnonymousBasketNotExists()
    {
        var anonymousBasket = null as Basket;
        var userBasket = new Basket(_existentUserBasketBuyerId);

        var results = new Results<Basket?>(anonymousBasket)
                        .Then(userBasket);

        _mockBasketRepo.FirstOrDefaultAsync(Arg.Any<BasketWithItemsSpecification>(), default).Returns(x => results.Next());

        var basketService = new BasketSvc(_mockBasketRepo, _mockLogger);

        await basketService.TransferBasketAsync(_nonexistentAnonymousBasketBuyerId, _existentUserBasketBuyerId);

        await _mockBasketRepo.Received().FirstOrDefaultAsync(Arg.Any<BasketWithItemsSpecification>(), default);
    }

    [Fact]
    public async Task TransferAnonymousBasketItemsWhilePreservingExistingUserBasketItems()
    {
        var anonymousBasket = new Basket(_existentAnonymousBasketBuyerId);
        anonymousBasket.AddItem(1, 10, 1);
        anonymousBasket.AddItem(3, 55, 7);
        var userBasket = new Basket(_existentUserBasketBuyerId);
        userBasket.AddItem(1, 10, 4);
        userBasket.AddItem(2, 99, 3);

        var results = new Results<Basket>(anonymousBasket)
                        .Then(userBasket);

        _mockBasketRepo.FirstOrDefaultAsync(Arg.Any<BasketWithItemsSpecification>(), default).Returns(x => results.Next());

        var basketService = new BasketSvc(_mockBasketRepo, _mockLogger);

        await basketService.TransferBasketAsync(_nonexistentAnonymousBasketBuyerId, _existentUserBasketBuyerId);

        await _mockBasketRepo.Received().UpdateAsync(userBasket, default);

        userBasket.Items.Should().HaveCount(3);
        userBasket.Items.Should().Contain(x => x.CatalogItemId == 1 && x.UnitPrice == 10 && x.Quantity == 5);
        userBasket.Items.Should().Contain(x => x.CatalogItemId == 2 && x.UnitPrice == 99 && x.Quantity == 3);
        userBasket.Items.Should().Contain(x => x.CatalogItemId == 3 && x.UnitPrice == 55 && x.Quantity == 7);
    }

    [Fact]
    public async Task RemovesAnonymousBasketAfterUpdatingUserBasket()
    {
        var anonymousBasket = new Basket(_existentAnonymousBasketBuyerId);
        var userBasket = new Basket(_existentUserBasketBuyerId);

        var results = new Results<Basket>(anonymousBasket)
                        .Then(userBasket);

        _mockBasketRepo.FirstOrDefaultAsync(Arg.Any<BasketWithItemsSpecification>(), default).Returns(x => results.Next());

        var basketService = new BasketSvc(_mockBasketRepo, _mockLogger);

        await basketService.TransferBasketAsync(_nonexistentAnonymousBasketBuyerId, _existentUserBasketBuyerId);

        await _mockBasketRepo.Received().UpdateAsync(userBasket, default);
        await _mockBasketRepo.Received().DeleteAsync(anonymousBasket, default);
    }

    [Fact]
    public async Task CreatesNewUserBasketIfNotExists()
    {
        var anonymousBasket = new Basket(_existentAnonymousBasketBuyerId);
        var userBasket = null as Basket;

        var results = new Results<Basket?>(anonymousBasket)
                        .Then(userBasket);

        _mockBasketRepo.FirstOrDefaultAsync(Arg.Any<BasketWithItemsSpecification>(), default).Returns(x => results.Next());

        var basketService = new BasketSvc(_mockBasketRepo, _mockLogger);

        await basketService.TransferBasketAsync(_existentAnonymousBasketBuyerId, _nonexistentUserBasketBuyerId);

        await _mockBasketRepo.Received().AddAsync(Arg.Is<Basket>(x => x.BuyerId == _nonexistentUserBasketBuyerId), default);
    }
}
