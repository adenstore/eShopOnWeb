using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BasketAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data;

/// <summary>
/// The monolith's database context. Despite the name it no longer holds any catalog data: the catalog
/// lives in CatalogService (its own CatalogServiceDb), and the catalog tables were dropped by the
/// MoveCatalogToCatalogService migration. What is left here is the basket that still runs in the monolith.
/// The name is kept so the basket code and the existing migrations don't have to change.
/// </summary>
public class CatalogContext : DbContext
{
    #pragma warning disable CS8618 // Required by Entity Framework
    public CatalogContext(DbContextOptions<CatalogContext> options) : base(options) {}

    public DbSet<Basket> Baskets { get; set; }
    public DbSet<BasketItem> BasketItems { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}
