using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Data;

/// <summary>
/// Applies CatalogContext's migrations at startup. This used to happen inside CatalogContextSeed, which was
/// deleted together with the monolith's catalog code (the catalog now lives in CatalogService). The migrations
/// still have to run: they create the monolith's basket tables and drop the old catalog tables
/// (MoveCatalogToCatalogService). Retries up to 10 times like the old seed did, in case SQL Server isn't ready yet.
/// </summary>
public static class CatalogContextMigrator
{
    public static async Task MigrateAsync(CatalogContext catalogContext, ILogger logger, int retry = 0)
    {
        try
        {
            if (catalogContext.Database.IsSqlServer())
            {
                await catalogContext.Database.MigrateAsync();
            }
        }
        catch (Exception ex)
        {
            if (retry >= 10) throw;

            logger.LogError(ex.Message);
            await MigrateAsync(catalogContext, logger, retry + 1);
        }
    }
}
