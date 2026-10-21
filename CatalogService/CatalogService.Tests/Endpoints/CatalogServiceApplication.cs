using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.CatalogService.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Microsoft.eShopWeb.CatalogService.Tests.Endpoints;

/// <summary>
/// Starts CatalogService.Api in memory with an in-memory database seeded with the normal catalog data.
/// Each test class gets its own instance through IClassFixture, and each instance its own database.
/// </summary>
public class CatalogServiceApplication : WebApplicationFactory<Program>
{
    // EF Core shares in-memory databases with the same name across the whole test run,
    // so a unique name keeps one test class's changes out of another's.
    private readonly string _databaseName = $"CatalogTests-{Guid.NewGuid()}";

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureHostConfiguration(config =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["UseOnlyInMemoryDatabase"] = "true"
            }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<CatalogContext>>();
            services.AddDbContext<CatalogContext>(options => options.UseInMemoryDatabase(_databaseName));
        });

        return base.CreateHost(builder);
    }
}
