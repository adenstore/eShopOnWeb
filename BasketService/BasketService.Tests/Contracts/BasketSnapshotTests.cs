using Microsoft.eShopWeb.BasketService.Contracts;
using Microsoft.eShopWeb.BasketService.Tests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.BasketService.Tests.Contracts;

public class BasketSnapshotTests
{
    [Fact]
    public void CarriesTheWholeBasketSoReadersCanApplyItBlindly()
    {
        var builder = new BasketBuilder();
        var basket = builder.WithOneBasketItem();

        var snapshot = BasketSnapshot.FromBasket(basket);

        Assert.Equal(basket.BuyerId, snapshot.BuyerId);
        var item = Assert.Single(snapshot.Items);
        Assert.Equal(2, item.CatalogItemId);
        Assert.Equal(3.40m, item.UnitPrice);
        Assert.Equal(4, item.Quantity);
    }

    [Fact]
    public void TotalItemsCountsUnitsNotLines()
    {
        // This is the number the layout's basket component renders on every page, and it is served
        // from the snapshot precisely so that rendering costs no round-trip.
        var basket = new BasketBuilder().WithOneBasketItem();
        basket.AddItem(3, 1.00m, 2);

        Assert.Equal(6, BasketSnapshot.FromBasket(basket).TotalItems);
    }

    [Fact]
    public void AnEmptySnapshotStandsInForADeletedBasket()
    {
        var snapshot = BasketSnapshot.Empty(basketId: 42, buyerId: "buyer@example.com");

        Assert.Equal(42, snapshot.BasketId);
        Assert.Equal("buyer@example.com", snapshot.BuyerId);
        Assert.Empty(snapshot.Items);
        Assert.Equal(0, snapshot.TotalItems);
    }
}
