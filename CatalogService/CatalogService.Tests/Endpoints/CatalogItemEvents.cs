using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.eShopWeb.CatalogService.Api.Endpoints.CatalogItemEndpoints;
using Microsoft.eShopWeb.CatalogService.Contracts.Events;
using Xunit;

namespace Microsoft.eShopWeb.CatalogService.Tests.Endpoints;

/// <summary>
/// Every successful change to the catalog publishes an integration event for other services;
/// a refused or failed request publishes nothing.
/// </summary>
public class CatalogItemEvents : IClassFixture<CatalogServiceApplication>
{
    private readonly CatalogServiceApplication _factory;
    private readonly HttpClient _admin;

    public CatalogItemEvents(CatalogServiceApplication factory)
    {
        _factory = factory;
        _admin = factory.CreateClient();
        _admin.DefaultRequestHeaders.Authorization = ApiTokenHelper.AdminUser();
    }

    private IEnumerable<TEvent> Published<TEvent>() => _factory.Events.Published.OfType<TEvent>();

    private async Task<int> CreateItemAsync(string name, decimal price = 4.56m)
    {
        var response = await _admin.PostAsJsonAsync("api/catalog-items", new CreateCatalogItemRequest
        {
            CatalogBrandId = 1,
            CatalogTypeId = 2,
            Name = name,
            Description = $"{name} description",
            Price = price
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreateCatalogItemResponse>())!.CatalogItem.Id;
    }

    [Fact]
    public async Task Create_PublishesCatalogItemCreated()
    {
        var id = await CreateItemAsync("Event mug", 12.34m);

        Published<CatalogItemCreatedIntegrationEvent>().Should().ContainSingle(e => e.CatalogItemId == id)
            .Which.Should().BeEquivalentTo(new
            {
                Name = "Event mug",
                Description = "Event mug description",
                Price = 12.34m,
                CatalogBrandId = 1,
                CatalogTypeId = 2
            });
    }

    [Fact]
    public async Task Update_PublishesCatalogItemUpdated_WithTheNewValues()
    {
        var id = await CreateItemAsync("Mug before update");

        await _admin.PutAsJsonAsync("api/catalog-items", new UpdateCatalogItemRequest
        {
            Id = id,
            CatalogBrandId = 3,
            CatalogTypeId = 4,
            Name = "Mug after update",
            Description = "New description",
            Price = 99.95m
        });

        Published<CatalogItemUpdatedIntegrationEvent>().Should().ContainSingle(e => e.CatalogItemId == id)
            .Which.Should().BeEquivalentTo(new
            {
                Name = "Mug after update",
                Description = "New description",
                Price = 99.95m,
                CatalogBrandId = 3,
                CatalogTypeId = 4
            });
    }

    [Fact]
    public async Task Delete_PublishesCatalogItemDeleted()
    {
        var id = await CreateItemAsync("Mug to delete");

        await _admin.DeleteAsync($"api/catalog-items/{id}");

        Published<CatalogItemDeletedIntegrationEvent>().Should().ContainSingle(e => e.CatalogItemId == id);
    }

    [Fact]
    public async Task EachEvent_HasItsOwnIdAndTimestamp()
    {
        var id = await CreateItemAsync("Mug with ids");
        await _admin.DeleteAsync($"api/catalog-items/{id}");

        var created = Published<CatalogItemCreatedIntegrationEvent>().Single(e => e.CatalogItemId == id);
        var deleted = Published<CatalogItemDeletedIntegrationEvent>().Single(e => e.CatalogItemId == id);

        created.EventId.Should().NotBe(Guid.Empty).And.NotBe(deleted.EventId);
        created.OccurredAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        deleted.OccurredAt.Should().BeOnOrAfter(created.OccurredAt);
    }

    [Fact]
    public async Task RefusedRequests_PublishNothing()
    {
        var normalUser = _factory.CreateClient();
        normalUser.DefaultRequestHeaders.Authorization = ApiTokenHelper.NormalUser();

        (await normalUser.PostAsJsonAsync("api/catalog-items", new CreateCatalogItemRequest { Name = "Not allowed", Description = "x", Price = 1, CatalogBrandId = 1, CatalogTypeId = 1 }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _admin.PostAsJsonAsync("api/catalog-items", new CreateCatalogItemRequest { Name = ".NET Bot Black Sweatshirt", Description = "x", Price = 1, CatalogBrandId = 1, CatalogTypeId = 1 }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _admin.DeleteAsync("api/catalog-items/0"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        Published<CatalogItemCreatedIntegrationEvent>().Should().NotContain(e => e.Name == "Not allowed" || e.Name == ".NET Bot Black Sweatshirt");
        Published<CatalogItemDeletedIntegrationEvent>().Should().NotContain(e => e.CatalogItemId == 0);
    }
}
