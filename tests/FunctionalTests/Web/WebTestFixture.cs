using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Web.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.FunctionalTests.Web;

public class TestApplication : WebApplicationFactory<IBasketViewModelService>
{
    private readonly string _environment = "Development";

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseEnvironment(_environment);

        // Add mock/test services to the builder here
        builder.ConfigureServices(services =>
        {
            var descriptors = services.Where(d =>
                                                d.ServiceType == typeof(DbContextOptions<CatalogContext>) ||
                                                d.ServiceType == typeof(DbContextOptions<AppIdentityDbContext>))
                                            .ToList();

            foreach (var descriptor in descriptors)
            {
                services.Remove(descriptor);
            }

            services.AddScoped(sp =>
            {
                // Replace SQLite with in-memory database for tests
                return new DbContextOptionsBuilder<CatalogContext>()
                .UseInMemoryDatabase("InMemoryDbForTesting")
                .UseApplicationServiceProvider(sp)
                .Options;
            });
            services.AddScoped(sp =>
            {
                // Replace SQLite with in-memory database for tests
                return new DbContextOptionsBuilder<AppIdentityDbContext>()
                .UseInMemoryDatabase("Identity")
                .UseApplicationServiceProvider(sp)
                .Options;
            });

            // Replace the HTTP client for the Order microservice with an in-memory fake
            services.RemoveAll<IOrderApiClient>();
            services.AddSingleton<IOrderApiClient, FakeOrderApiClient>();

            // Production now registers BasketApiClient, which calls the ApiGateway. These tests
            // substitute the in-process basket against the in-memory database above, so they need
            // no live gateway, service or broker and still exercise real basket rules. It behaves
            // like the gateway where it matters to these tests: a read does not create a basket,
            // so a buyer who has added nothing is reported at NoBasketId, the same as production.
            // When that implementation is deleted this becomes a fake implementing both
            // interfaces, the way FakeOrderApiClient and FakeCatalogApiClient already do.
            services.RemoveAll<IBasketService>();
            services.RemoveAll<IBasketQueryService>();
            services.AddScoped<IBasketService, ApplicationCore.Services.BasketService>();
            services.AddScoped<IBasketQueryService, Infrastructure.Data.Queries.BasketQueryService>();
            // Replace the HTTP client for the Catalog microservice with an in-memory fake
            services.RemoveAll<ICatalogApiClient>();
            services.AddSingleton<ICatalogApiClient, FakeCatalogApiClient>();
        });

        return base.CreateHost(builder);
    }
}
