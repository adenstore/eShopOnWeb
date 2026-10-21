using FluentAssertions;
using Microsoft.eShopWeb.BasketService.Domain;
using Microsoft.eShopWeb.BasketService.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.BasketServiceTests.Specifications;

public class BasketWithItems
{
    private readonly int _testBasketId = 123;
    private readonly string _buyerId = "Test buyerId";

    [Fact]
    public void MatchesBasketWithGivenBasketId()
    {
        var spec = new BasketWithItemsSpecification(_testBasketId);

        var result = spec.Evaluate(GetTestBasketCollection()).FirstOrDefault();

        result.Should().NotBeNull();
        result!.Id.Should().Be(_testBasketId);
    }

    [Fact]
    public void MatchesNoBasketsIfBasketIdNotPresent()
    {
        var badBasketId = -1;
        var spec = new BasketWithItemsSpecification(badBasketId);

        var result = spec.Evaluate(GetTestBasketCollection());

        result.Should().BeEmpty();
    }

    [Fact]
    public void MatchesBasketWithGivenBuyerId()
    {
        var spec = new BasketWithItemsSpecification(_buyerId);

        var result = spec.Evaluate(GetTestBasketCollection()).FirstOrDefault();

        result.Should().NotBeNull();
        result!.BuyerId.Should().Be(_buyerId);
    }

    [Fact]
    public void MatchesNoBasketsIfBuyerIdNotPresent()
    {
        var badBuyerId = "badBuyerId";
        var spec = new BasketWithItemsSpecification(badBuyerId);

        var result = spec.Evaluate(GetTestBasketCollection());

        result.Should().BeEmpty();
    }

    private List<Basket> GetTestBasketCollection()
    {
        var basket1Mock = Substitute.For<Basket>(_buyerId);
        basket1Mock.Id.Returns(1);
        var basket2Mock = Substitute.For<Basket>(_buyerId);
        basket2Mock.Id.Returns(2);
        var basket3Mock = Substitute.For<Basket>(_buyerId);
        basket3Mock.Id.Returns(_testBasketId);

        return new List<Basket>()
        {
            basket1Mock,
            basket2Mock,
            basket3Mock
        };
    }
}
