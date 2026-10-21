using FluentAssertions;
using Microsoft.eShopWeb.CatalogService.Domain.Entities;
using Microsoft.eShopWeb.CatalogService.Domain.Specifications;
using Microsoft.eShopWeb.CatalogService.Tests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.CatalogService.Tests.Specifications;

public class CatalogSpecifications
{
    // brand/type pairs: (1,1) (2,1) (3,1) (1,2) (2,2)
    private static readonly List<CatalogItem> Items = new()
    {
        new CatalogItemBuilder().WithId(1).WithBrand(1).WithType(1).WithName("Mug").Build(),
        new CatalogItemBuilder().WithId(2).WithBrand(2).WithType(1).WithName("Cup").Build(),
        new CatalogItemBuilder().WithId(3).WithBrand(3).WithType(1).WithName("Glass").Build(),
        new CatalogItemBuilder().WithId(4).WithBrand(1).WithType(2).WithName("T-Shirt").Build(),
        new CatalogItemBuilder().WithId(5).WithBrand(2).WithType(2).WithName("Sweatshirt").Build()
    };

    [Theory]
    [InlineData(null, null, 5)]
    [InlineData(1, null, 2)]
    [InlineData(null, 1, 3)]
    [InlineData(2, 2, 1)]
    [InlineData(3, 2, 0)]
    public void Filter_ReturnsItemsMatchingBrandAndType(int? brandId, int? typeId, int expectedCount)
    {
        var result = new CatalogFilterSpecification(brandId, typeId).Evaluate(Items).ToList();

        result.Should().HaveCount(expectedCount)
            .And.OnlyContain(i => (brandId == null || i.CatalogBrandId == brandId) && (typeId == null || i.CatalogTypeId == typeId));
    }

    [Fact]
    public void FilterPaginated_SkipsAndTakesWithinTheFilteredItems()
    {
        var result = new CatalogFilterPaginatedSpecification(skip: 1, take: 2, brandId: null, typeId: 1).Evaluate(Items).ToList();

        result.Select(i => i.Name).Should().Equal("Cup", "Glass");
    }

    [Fact]
    public void FilterPaginated_ReturnsEverything_WhenTakeIsZero()
    {
        var result = new CatalogFilterPaginatedSpecification(skip: 0, take: 0, brandId: null, typeId: null).Evaluate(Items);

        result.Should().HaveCount(Items.Count);
    }

    [Fact]
    public void ItemsById_ReturnsOnlyTheRequestedItems()
    {
        var result = new CatalogItemsSpecification(1, 4, 99).Evaluate(Items);

        result.Select(i => i.Id).Should().BeEquivalentTo(new[] { 1, 4 });
    }

    [Fact]
    public void ItemName_MatchesTheExactName()
    {
        var result = new CatalogItemNameSpecification("Cup").Evaluate(Items);

        result.Should().ContainSingle().Which.Id.Should().Be(2);
    }
}
