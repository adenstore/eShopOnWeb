using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.eShopWeb.CatalogService.Api.Endpoints.CatalogItemEndpoints;
using Xunit;

namespace Microsoft.eShopWeb.CatalogService.Tests.Endpoints;

/// <summary>
/// Read-only calls against the seeded catalog (12 items, 5 brands, 4 types).
/// </summary>
public class CatalogItemQueries : IClassFixture<CatalogServiceApplication>
{
    private const int SeededItemCount = 12;
    private readonly HttpClient _client;

    public CatalogItemQueries(CatalogServiceApplication factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetById_ReturnsTheItem()
    {
        var response = await _client.GetFromJsonAsync<GetByIdCatalogItemResponse>("api/catalog-items/5");

        response!.CatalogItem.Id.Should().Be(5);
        response.CatalogItem.Name.Should().Be("Roslyn Red Sheet");
        response.CatalogItem.PictureUri.Should().Be("/images/products/5.png", "the placeholder host is replaced by CatalogBaseUrl");
    }

    [Fact]
    public async Task GetById_Returns404_WhenItemDoesNotExist()
    {
        var response = await _client.GetAsync("api/catalog-items/0");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListPaged_ReturnsOnePageWithTotals()
    {
        var response = await _client.GetFromJsonAsync<ListPagedCatalogItemResponse>("api/catalog-items?pageSize=10&pageIndex=0");

        response!.CatalogItems.Should().HaveCount(10);
        response.TotalItems.Should().Be(SeededItemCount);
        response.PageCount.Should().Be(2);
    }

    [Fact]
    public async Task ListPaged_ReturnsTheRemainingItemsOnTheLastPage()
    {
        var response = await _client.GetFromJsonAsync<ListPagedCatalogItemResponse>("api/catalog-items?pageSize=10&pageIndex=1");

        response!.CatalogItems.Should().HaveCount(SeededItemCount - 10);
    }

    [Fact]
    public async Task ListPaged_ReturnsAllItems_WhenNoPageSizeIsGiven()
    {
        var response = await _client.GetFromJsonAsync<ListPagedCatalogItemResponse>("api/catalog-items");

        response!.CatalogItems.Should().HaveCount(SeededItemCount);
        response.PageCount.Should().Be(1);
    }

    [Fact]
    public async Task ListPaged_FiltersByBrandAndType()
    {
        // Brand 2 = ".NET", type 1 = "Mug"
        var response = await _client.GetFromJsonAsync<ListPagedCatalogItemResponse>("api/catalog-items?catalogBrandId=2&catalogTypeId=1");

        response!.CatalogItems.Should().ContainSingle()
            .Which.Name.Should().Be(".NET Black & White Mug");
        response.TotalItems.Should().Be(1);
    }

    [Fact]
    public async Task Lookups_ReturnTheSeededBrandsAndTypes()
    {
        var brands = await _client.GetFromJsonAsync<Api.Endpoints.CatalogBrandEndpoints.ListCatalogBrandsResponse>("api/catalog-brands");
        var types = await _client.GetFromJsonAsync<Api.Endpoints.CatalogTypeEndpoints.ListCatalogTypesResponse>("api/catalog-types");

        brands!.CatalogBrands.Select(b => b.Name).Should().BeEquivalentTo(".NET", "Azure", "Other", "SQL Server", "Visual Studio");
        types!.CatalogTypes.Select(t => t.Name).Should().BeEquivalentTo("Mug", "Sheet", "T-Shirt", "USB Memory Stick");
    }
}
