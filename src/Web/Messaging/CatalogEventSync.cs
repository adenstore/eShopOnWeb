using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.CatalogService.Contracts.Events;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.Web.Messaging;

/// <summary>
/// Applies catalog events from CatalogService to the monolith's own copy of the catalog (CatalogDb).
///
/// Temporary bridge for the strangler migration: the basket page, BasketViewModelService and
/// OrderCheckoutService still read names, prices and pictures from CatalogDb. Keeping it in sync lets them
/// see items created or changed in CatalogService. Delete this (and CatalogEventsListener) together with
/// the monolith's catalog code, once nothing reads CatalogDb anymore.
///
/// Every operation is idempotent, so a repeated or out-of-order event can't break anything:
/// "created" for an existing item updates it, "updated" for an unknown item inserts it,
/// and "deleted" for an unknown item is ignored.
/// </summary>
public class CatalogEventSync
{
    private readonly CatalogContext _catalogContext;
    private readonly ILogger<CatalogEventSync> _logger;

    public CatalogEventSync(CatalogContext catalogContext, ILogger<CatalogEventSync> logger)
    {
        _catalogContext = catalogContext;
        _logger = logger;
    }

    public Task ApplyAsync(CatalogItemCreatedIntegrationEvent e, CancellationToken cancellationToken = default) =>
        UpsertAsync(e.CatalogItemId, e.Name, e.Description, e.Price, e.PictureUri, e.CatalogBrandId, e.CatalogTypeId, cancellationToken);

    public Task ApplyAsync(CatalogItemUpdatedIntegrationEvent e, CancellationToken cancellationToken = default) =>
        UpsertAsync(e.CatalogItemId, e.Name, e.Description, e.Price, e.PictureUri, e.CatalogBrandId, e.CatalogTypeId, cancellationToken);

    public async Task ApplyAsync(CatalogItemDeletedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        var item = await _catalogContext.CatalogItems.FirstOrDefaultAsync(i => i.Id == e.CatalogItemId, cancellationToken);
        if (item is null)
        {
            _logger.LogInformation("Catalog item {CatalogItemId} is already gone from CatalogDb", e.CatalogItemId);
            return;
        }

        _catalogContext.CatalogItems.Remove(item);
        await _catalogContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Removed catalog item {CatalogItemId} from CatalogDb", e.CatalogItemId);
    }

    private async Task UpsertAsync(int id, string name, string description, decimal price, string pictureUri,
        int catalogBrandId, int catalogTypeId, CancellationToken cancellationToken)
    {
        var item = await _catalogContext.CatalogItems.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            item = new CatalogItem(catalogTypeId, catalogBrandId, description, name, price, pictureUri);
            // The copy must use CatalogService's id, so basket and orders refer to the same item.
            // Id has a protected setter, so it is set through EF before the item is added.
            _catalogContext.Entry(item).Property(i => i.Id).CurrentValue = id;
            _catalogContext.CatalogItems.Add(item);
            _logger.LogInformation("Added catalog item {CatalogItemId} to CatalogDb", id);
        }
        else
        {
            // Plain copy of CatalogService's values (already validated there), so the entity's
            // setters are bypassed the same way: PictureUri is stored as-is, for example.
            var entry = _catalogContext.Entry(item);
            entry.Property(i => i.Name).CurrentValue = name;
            entry.Property(i => i.Description).CurrentValue = description;
            entry.Property(i => i.Price).CurrentValue = price;
            entry.Property(i => i.PictureUri).CurrentValue = pictureUri;
            entry.Property(i => i.CatalogBrandId).CurrentValue = catalogBrandId;
            entry.Property(i => i.CatalogTypeId).CurrentValue = catalogTypeId;
            _logger.LogInformation("Updated catalog item {CatalogItemId} in CatalogDb", id);
        }

        await _catalogContext.SaveChangesAsync(cancellationToken);
    }
}
