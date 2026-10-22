using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.CatalogService.Contracts.Events;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Web.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Catalog;

/// <summary>
/// CatalogEventSync keeps the monolith's CatalogDb in step with CatalogService's events.
/// </summary>
public class CatalogEventSyncTests
{
    private readonly CatalogContext _catalogContext;
    private readonly CatalogEventSync _sync;

    public CatalogEventSyncTests()
    {
        var dbOptions = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(databaseName: $"CatalogSync-{Guid.NewGuid()}")
            .Options;
        _catalogContext = new CatalogContext(dbOptions);
        _sync = new CatalogEventSync(_catalogContext, NullLogger<CatalogEventSync>.Instance);
    }

    private static CatalogItemCreatedIntegrationEvent Created(int id, string name = "Synced mug", decimal price = 9.99m) =>
        new(Guid.NewGuid(), DateTimeOffset.UtcNow, id, name, $"{name} description", price, "images\\products\\mug.png", 2, 1);

    private static CatalogItemUpdatedIntegrationEvent Updated(int id, string name, decimal price) =>
        new(Guid.NewGuid(), DateTimeOffset.UtcNow, id, name, $"{name} description", price, "images\\products\\new.png", 3, 2);

    private async Task<Microsoft.eShopWeb.ApplicationCore.Entities.CatalogItem?> FindAsync(int id)
    {
        _catalogContext.ChangeTracker.Clear();
        return await _catalogContext.CatalogItems.FirstOrDefaultAsync(i => i.Id == id);
    }

    [Fact]
    public async Task Created_AddsTheItemWithCatalogServicesId()
    {
        await _sync.ApplyAsync(Created(1021, "New mug", 12.50m));

        var item = await FindAsync(1021);
        Assert.NotNull(item);
        Assert.Equal("New mug", item.Name);
        Assert.Equal(12.50m, item.Price);
        Assert.Equal("images\\products\\mug.png", item.PictureUri);
        Assert.Equal(2, item.CatalogBrandId);
        Assert.Equal(1, item.CatalogTypeId);
    }

    [Fact]
    public async Task Created_Twice_DoesNotDuplicateTheItem()
    {
        await _sync.ApplyAsync(Created(1021));
        await _sync.ApplyAsync(Created(1021, "Same mug, replayed", 11m));

        Assert.Equal(1, await _catalogContext.CatalogItems.CountAsync(i => i.Id == 1021));
        Assert.Equal("Same mug, replayed", (await FindAsync(1021))!.Name);
    }

    [Fact]
    public async Task Updated_OverwritesNamePriceAndPicture()
    {
        await _sync.ApplyAsync(Created(1021));

        await _sync.ApplyAsync(Updated(1021, "Renamed mug", 14.50m));

        var item = await FindAsync(1021);
        Assert.Equal("Renamed mug", item!.Name);
        Assert.Equal(14.50m, item.Price);
        Assert.Equal("images\\products\\new.png", item.PictureUri);
        Assert.Equal(3, item.CatalogBrandId);
        Assert.Equal(2, item.CatalogTypeId);
    }

    [Fact]
    public async Task Updated_ForAnUnknownItem_AddsIt()
    {
        await _sync.ApplyAsync(Updated(2042, "Missed the created event", 5m));

        Assert.Equal("Missed the created event", (await FindAsync(2042))!.Name);
    }

    [Fact]
    public async Task Deleted_RemovesTheItem()
    {
        await _sync.ApplyAsync(Created(1021));

        await _sync.ApplyAsync(new CatalogItemDeletedIntegrationEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, 1021));

        Assert.Null(await FindAsync(1021));
    }

    [Fact]
    public async Task Deleted_ForAnUnknownItem_DoesNothing()
    {
        await _sync.ApplyAsync(new CatalogItemDeletedIntegrationEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, 9999));

        Assert.Equal(0, await _catalogContext.CatalogItems.CountAsync());
    }
}
