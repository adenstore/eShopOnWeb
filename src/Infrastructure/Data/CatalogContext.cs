using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.eShopWeb.Infrastructure.Data;

/// <summary>
/// The monolith's database context, now holding nothing at all. The catalog moved to CatalogService,
/// the orders to OrderService and the basket to BasketService, and each move dropped its tables
/// (MoveCatalogToCatalogService, MoveOrdersToOrderService, DropBasketTables).
/// <para>
/// It is kept, empty, because the migration that drops the last of those tables has to be applied
/// by something, and the <c>__EFMigrationsHistory</c> table that records which migrations have run
/// belongs to this context. The database it points at — <c>CatalogConnection</c> — now contains
/// only that history. Once there is no deployment left running an older migration state, this
/// context, its migrations, <c>EfRepository</c> and the <c>IRepository</c>/<c>IReadRepository</c>
/// registrations in <c>ConfigureCoreServices</c> and <c>PublicApi</c> can all go together; they
/// already have no consumer. The name is kept so the existing migrations don't have to change.
/// </para>
/// </summary>
public class CatalogContext : DbContext
{
    #pragma warning disable CS8618 // Required by Entity Framework
    public CatalogContext(DbContextOptions<CatalogContext> options) : base(options) {}

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}
