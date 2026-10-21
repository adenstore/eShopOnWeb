using FluentAssertions;
using Microsoft.eShopWeb.CatalogService.Domain.Entities;
using Microsoft.eShopWeb.CatalogService.Tests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.CatalogService.Tests.Domain;

public class CatalogItemUpdates
{
    private readonly CatalogItem _item = new CatalogItemBuilder().WithName("Old name").Build();

    [Fact]
    public void UpdateDetails_ChangesNameDescriptionAndPrice()
    {
        _item.UpdateDetails(new CatalogItem.CatalogItemDetails("New name", "New description", 9.99m));

        _item.Name.Should().Be("New name");
        _item.Description.Should().Be("New description");
        _item.Price.Should().Be(9.99m);
    }

    [Theory]
    [InlineData(null, "Description", 1)]
    [InlineData("", "Description", 1)]
    [InlineData("Name", null, 1)]
    [InlineData("Name", "", 1)]
    [InlineData("Name", "Description", 0)]
    [InlineData("Name", "Description", -1)]
    public void UpdateDetails_Throws_WhenDetailsAreInvalid(string? name, string? description, decimal price)
    {
        var act = () => _item.UpdateDetails(new CatalogItem.CatalogItemDetails(name, description, price));

        act.Should().Throw<ArgumentException>();
        _item.Name.Should().Be("Old name", "an invalid update must not change the item");
    }

    [Fact]
    public void UpdateBrandAndType_ChangeTheIds()
    {
        _item.UpdateBrand(4);
        _item.UpdateType(3);

        _item.CatalogBrandId.Should().Be(4);
        _item.CatalogTypeId.Should().Be(3);
    }

    [Fact]
    public void UpdateBrand_Throws_WhenIdIsZero()
    {
        var act = () => _item.UpdateBrand(0);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UpdateType_Throws_WhenIdIsZero()
    {
        var act = () => _item.UpdateType(0);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UpdatePictureUri_PointsAtTheProductImagesFolder()
    {
        _item.UpdatePictureUri("mug.png");

        _item.PictureUri.Should().StartWith("images\\products\\mug.png");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void UpdatePictureUri_ClearsThePicture_WhenNameIsEmpty(string? pictureName)
    {
        _item.UpdatePictureUri(pictureName!);

        _item.PictureUri.Should().BeEmpty();
    }
}
