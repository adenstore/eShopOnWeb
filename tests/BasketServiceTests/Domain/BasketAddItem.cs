using FluentAssertions;
using Microsoft.eShopWeb.BasketService.Domain;
using Xunit;

namespace Microsoft.eShopWeb.BasketServiceTests.Domain;

public class BasketAddItem
{
    private readonly int _testCatalogItemId = 123;
    private readonly decimal _testUnitPrice = 1.23m;
    private readonly int _testQuantity = 2;
    private readonly string _buyerId = "Test buyerId";

    [Fact]
    public void AddsBasketItemIfNotPresent()
    {
        var basket = new Basket(_buyerId);
        basket.AddItem(_testCatalogItemId, _testUnitPrice, _testQuantity);

        var firstItem = basket.Items.Should().ContainSingle().Which;
        firstItem.CatalogItemId.Should().Be(_testCatalogItemId);
        firstItem.UnitPrice.Should().Be(_testUnitPrice);
        firstItem.Quantity.Should().Be(_testQuantity);
    }

    [Fact]
    public void IncrementsQuantityOfItemIfPresent()
    {
        var basket = new Basket(_buyerId);
        basket.AddItem(_testCatalogItemId, _testUnitPrice, _testQuantity);
        basket.AddItem(_testCatalogItemId, _testUnitPrice, _testQuantity);

        basket.Items.Should().ContainSingle()
            .Which.Quantity.Should().Be(_testQuantity * 2);
    }

    [Fact]
    public void KeepsOriginalUnitPriceIfMoreItemsAdded()
    {
        var basket = new Basket(_buyerId);
        basket.AddItem(_testCatalogItemId, _testUnitPrice, _testQuantity);
        basket.AddItem(_testCatalogItemId, _testUnitPrice * 2, _testQuantity);

        basket.Items.Should().ContainSingle()
            .Which.UnitPrice.Should().Be(_testUnitPrice);
    }

    [Fact]
    public void DefaultsToQuantityOfOne()
    {
        var basket = new Basket(_buyerId);
        basket.AddItem(_testCatalogItemId, _testUnitPrice);

        basket.Items.Should().ContainSingle()
            .Which.Quantity.Should().Be(1);
    }

    [Fact]
    public void CantAddItemWithNegativeQuantity()
    {
        var basket = new Basket(_buyerId);

        var addItem = () => basket.AddItem(_testCatalogItemId, _testUnitPrice, -1);

        addItem.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void CantModifyQuantityToNegativeNumber()
    {
        var basket = new Basket(_buyerId);
        basket.AddItem(_testCatalogItemId, _testUnitPrice);

        var addItem = () => basket.AddItem(_testCatalogItemId, _testUnitPrice, -2);

        addItem.Should().Throw<ArgumentOutOfRangeException>();
    }
}
