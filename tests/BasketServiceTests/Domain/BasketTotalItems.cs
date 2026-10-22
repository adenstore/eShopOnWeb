using FluentAssertions;
using Microsoft.eShopWeb.BasketService.Domain;
using Xunit;

namespace Microsoft.eShopWeb.BasketServiceTests.Domain;

public class BasketTotalItems
{
    private readonly int _testCatalogItemId = 123;
    private readonly decimal _testUnitPrice = 1.23m;
    private readonly int _testQuantity = 2;
    private readonly string _buyerId = "Test buyerId";

    [Fact]
    public void ReturnsTotalQuantityWithOneItem()
    {
        var basket = new Basket(_buyerId);
        basket.AddItem(_testCatalogItemId, _testUnitPrice, _testQuantity);

        basket.TotalItems.Should().Be(_testQuantity);
    }

    [Fact]
    public void ReturnsTotalQuantityWithMultipleItems()
    {
        var basket = new Basket(_buyerId);
        basket.AddItem(_testCatalogItemId, _testUnitPrice, _testQuantity);
        basket.AddItem(_testCatalogItemId, _testUnitPrice, _testQuantity * 2);

        basket.TotalItems.Should().Be(_testQuantity * 3);
    }
}
