using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Dtos;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BasketAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.OrderService.Contracts;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.OrderCheckoutServiceTests;

public class CreateOrderAsync
{
    private readonly IBasketService _mockBasketService = Substitute.For<IBasketService>();
    // Commented out on purpose: OrderCheckoutService now reads product details from CatalogService through
    // ICatalogItemLookup instead of IRepository<CatalogItem>, and no longer uses IUriComposer, because
    // CatalogService returns a complete picture URL. Old lines are kept here for reference.
    //private readonly IRepository<CatalogItem> _mockItemRepo = Substitute.For<IRepository<CatalogItem>>();
    private readonly ICatalogItemLookup _mockCatalogItemLookup = Substitute.For<ICatalogItemLookup>();
    private readonly IOrderApiClient _mockOrderApiClient = Substitute.For<IOrderApiClient>();
    //private readonly IUriComposer _uriComposer = new UriComposer(new CatalogSettings { CatalogBaseUrl = "http://test" });
    private readonly AddressDto _address = new("123 Main St.", "Kent", "OH", "United States", "44240");

    //private OrderCheckoutService CreateService() =>
    //    new(_mockBasketService, _mockItemRepo, _mockOrderApiClient, _uriComposer);
    private OrderCheckoutService CreateService() =>
        new(_mockBasketService, _mockCatalogItemLookup, _mockOrderApiClient);

    [Fact]
    public async Task SendsBasketSnapshotToOrderService()
    {
        var basket = new Basket("buyer-1");
        basket.AddItem(7, 12.5m, 3);
        _mockBasketService.GetBasketAsync(Arg.Any<int>()).Returns(BasketDto.FromBasket(basket));
        // Commented out on purpose: the catalog item now comes from ICatalogItemLookup, with a complete picture URL.
        //_mockItemRepo.ListAsync(Arg.Any<CatalogItemsSpecification>(), default)
        //    .Returns(new List<CatalogItem> { new TestCatalogItem(7, "Mug", "http://catalogbaseurltobereplaced/images/products/7.png") });
        _mockCatalogItemLookup.GetCatalogItemsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(new List<CatalogItemDetails> { new(7, "Mug", "http://test/images/products/7.png") });
        _mockOrderApiClient.CreateOrderAsync(Arg.Any<CreateOrderRequest>(), Arg.Any<CancellationToken>()).Returns(99);

        var orderId = await CreateService().CreateOrderAsync(1, _address);

        Assert.Equal(99, orderId);
        await _mockOrderApiClient.Received(1).CreateOrderAsync(
            Arg.Is<CreateOrderRequest>(r =>
                r.BuyerId == "buyer-1" &&
                r.ShipToAddress == _address &&
                r.Items.Count == 1 &&
                r.Items[0].CatalogItemId == 7 &&
                r.Items[0].ProductName == "Mug" &&
                r.Items[0].PictureUri == "http://test/images/products/7.png" &&
                r.Items[0].UnitPrice == 12.5m &&
                r.Items[0].Units == 3),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ThrowsAndSendsNothingForEmptyBasket()
    {
        _mockBasketService.GetBasketAsync(Arg.Any<int>()).Returns(BasketDto.FromBasket(new Basket("buyer-1")));

        await Assert.ThrowsAsync<EmptyBasketOnCheckoutException>(() => CreateService().CreateOrderAsync(1, _address));

        await _mockOrderApiClient.DidNotReceiveWithAnyArgs().CreateOrderAsync(default!, default);
    }

    // Commented out on purpose: only needed to fake the monolith's CatalogItem entity, which checkout no longer uses.
    //private class TestCatalogItem : CatalogItem
    //{
    //    public TestCatalogItem(int id, string name, string pictureUri) : base(1, 1, "", name, 1m, pictureUri)
    //    {
    //        Id = id;
    //    }
    //}
}
