using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.eShopWeb.CatalogService.Api.Endpoints.CatalogItemEndpoints;
using Xunit;

namespace Microsoft.eShopWeb.CatalogService.Tests.Endpoints;

/// <summary>
/// Create, update and delete. Only administrators may change the catalog.
/// Each test works on its own item, so the tests don't depend on each other.
/// </summary>
public class CatalogItemCommands : IClassFixture<CatalogServiceApplication>
{
    private readonly CatalogServiceApplication _factory;

    public CatalogItemCommands(CatalogServiceApplication factory)
    {
        _factory = factory;
    }

    private HttpClient ClientFor(System.Net.Http.Headers.AuthenticationHeaderValue? user)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = user;
        return client;
    }

    private static CreateCatalogItemRequest NewItem(string name) => new()
    {
        CatalogBrandId = 1,
        CatalogTypeId = 2,
        Name = name,
        Description = $"{name} description",
        Price = 4.56m
    };

    [Fact]
    public async Task Create_Returns401_WithoutAToken()
    {
        var response = await ClientFor(null).PostAsJsonAsync("api/catalog-items", NewItem("No token"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Create_Returns403_ForANormalUser()
    {
        var response = await ClientFor(ApiTokenHelper.NormalUser()).PostAsJsonAsync("api/catalog-items", NewItem("Normal user"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Create_AddsTheItem_ForAnAdministrator()
    {
        var client = ClientFor(ApiTokenHelper.AdminUser());

        var response = await client.PostAsJsonAsync("api/catalog-items", NewItem("Created item"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await response.Content.ReadFromJsonAsync<CreateCatalogItemResponse>())!.CatalogItem;
        created.Should().BeEquivalentTo(new { CatalogBrandId = 1, CatalogTypeId = 2, Name = "Created item", Price = 4.56m });
        response.Headers.Location!.ToString().Should().Be($"api/catalog-items/{created.Id}");

        var fetched = await client.GetFromJsonAsync<GetByIdCatalogItemResponse>($"api/catalog-items/{created.Id}");
        fetched!.CatalogItem.Name.Should().Be("Created item");
    }

    [Fact]
    public async Task Create_Returns409_WhenTheNameAlreadyExists()
    {
        var response = await ClientFor(ApiTokenHelper.AdminUser()).PostAsJsonAsync("api/catalog-items", NewItem(".NET Bot Black Sweatshirt"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("already exists");
    }

    [Fact]
    public async Task Update_ChangesTheItem_ForAnAdministrator()
    {
        var client = ClientFor(ApiTokenHelper.AdminUser());
        var created = (await (await client.PostAsJsonAsync("api/catalog-items", NewItem("Item to update")))
            .Content.ReadFromJsonAsync<CreateCatalogItemResponse>())!.CatalogItem;

        var response = await client.PutAsJsonAsync("api/catalog-items", new UpdateCatalogItemRequest
        {
            Id = created.Id,
            CatalogBrandId = 3,
            CatalogTypeId = 4,
            Name = "Updated item",
            Description = "Updated description",
            Price = 7.89m
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await client.GetFromJsonAsync<GetByIdCatalogItemResponse>($"api/catalog-items/{created.Id}");
        fetched!.CatalogItem.Should().BeEquivalentTo(new
        {
            CatalogBrandId = 3,
            CatalogTypeId = 4,
            Name = "Updated item",
            Description = "Updated description",
            Price = 7.89m
        });
    }

    [Fact]
    public async Task Update_Returns404_WhenItemDoesNotExist()
    {
        var response = await ClientFor(ApiTokenHelper.AdminUser()).PutAsJsonAsync("api/catalog-items", new UpdateCatalogItemRequest
        {
            Id = 9999,
            CatalogBrandId = 1,
            CatalogTypeId = 1,
            Name = "Missing",
            Description = "Missing",
            Price = 1
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_RemovesTheItem_ForAnAdministrator()
    {
        var client = ClientFor(ApiTokenHelper.AdminUser());
        var created = (await (await client.PostAsJsonAsync("api/catalog-items", NewItem("Item to delete")))
            .Content.ReadFromJsonAsync<CreateCatalogItemResponse>())!.CatalogItem;

        var response = await client.DeleteAsync($"api/catalog-items/{created.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<DeleteCatalogItemResponse>())!.Status.Should().Be("Deleted");
        (await client.GetAsync($"api/catalog-items/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_Returns404_WhenItemDoesNotExist()
    {
        var response = await ClientFor(ApiTokenHelper.AdminUser()).DeleteAsync("api/catalog-items/0");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_Returns401_WithoutAToken()
    {
        var response = await ClientFor(null).DeleteAsync("api/catalog-items/1");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
