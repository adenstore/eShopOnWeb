using Microsoft.eShopWeb.BasketService.Contracts;
using Microsoft.eShopWeb.BasketService.Messaging;
using Xunit;

namespace Microsoft.eShopWeb.BasketServiceTests.Messaging;

/// <summary>
/// The domain services carry no guard clauses for these values — [ApiController] used to reject a
/// bad body before the action ran. These tests are what now holds that line.
/// </summary>
public class MessageValidation
{
    [Fact]
    public void AcceptsAWellFormedAddItemCommand()
    {
        var command = new AddItemToBasketCommand
        {
            BuyerId = "buyer@example.com",
            CatalogItemId = 3,
            UnitPrice = 12.50m,
            Quantity = 2
        };

        Assert.True(MessageValidator.TryValidate(command, out _));
    }

    [Theory]
    [InlineData("", 3, 1, "buyer id cannot be empty")]
    [InlineData("buyer", 0, 1, "catalog item id must be a real id")]
    [InlineData("buyer", 3, 0, "adding zero of something is not an add")]
    [InlineData("buyer", 3, -1, "a negative quantity would silently remove items")]
    public void RejectsAddItemCommandsTheDomainHasNoGuardFor(string buyerId, int catalogItemId, int quantity, string why)
    {
        var command = new AddItemToBasketCommand
        {
            BuyerId = buyerId,
            CatalogItemId = catalogItemId,
            UnitPrice = 1m,
            Quantity = quantity
        };

        Assert.False(MessageValidator.TryValidate(command, out var error), why);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void RejectsANegativeUnitPrice()
    {
        var command = new AddItemToBasketCommand
        {
            BuyerId = "buyer",
            CatalogItemId = 3,
            UnitPrice = -0.01m,
            Quantity = 1
        };

        Assert.False(MessageValidator.TryValidate(command, out _));
    }

    [Fact]
    public void AcceptsAnEmptyQuantitiesMap()
    {
        // An empty map is a valid no-op, not a malformed message: the page can post a basket with
        // nothing changed, and dead-lettering that would be wrong.
        var command = new SetBasketQuantitiesCommand { BasketId = 1, Quantities = new Dictionary<string, int>() };

        Assert.True(MessageValidator.TryValidate(command, out _));
    }

    [Fact]
    public void RejectsSetQuantitiesWithoutABasketId()
    {
        var command = new SetBasketQuantitiesCommand { BasketId = 0 };

        Assert.False(MessageValidator.TryValidate(command, out _));
    }

    [Fact]
    public void RejectsATransferMissingEitherSide()
    {
        Assert.False(MessageValidator.TryValidate(
            new TransferBasketCommand { AnonymousId = "cookie-id", UserName = "" }, out _));
        Assert.False(MessageValidator.TryValidate(
            new TransferBasketCommand { AnonymousId = "", UserName = "buyer@example.com" }, out _));
    }

    [Fact]
    public void RejectsAQueryForNoOne()
    {
        Assert.False(MessageValidator.TryValidate(new GetBasketByBuyerRequest { BuyerId = "" }, out _));
        Assert.False(MessageValidator.TryValidate(new GetBasketByIdRequest { BasketId = 0 }, out _));
    }
}
