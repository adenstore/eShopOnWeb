using MediatR;
using Microsoft.eShopWeb.Web.Interfaces;
using Microsoft.eShopWeb.Web.Services;

namespace Microsoft.eShopWeb.Web.Configuration;

public static class ConfigureWebServices
{
    public static IServiceCollection AddWebServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMediatR(cfg => 
            cfg.RegisterServicesFromAssembly(typeof(BasketViewModelService).Assembly));
        services.AddScoped<IBasketViewModelService, BasketViewModelService>();
        // Commented out on purpose: CatalogItemViewModelService is only used by the old, unreachable Razor admin
        // page Pages/Admin/EditCatalogItem, which is commented out because it writes to the monolith's CatalogDb
        // and would break once the monolith's catalog code is removed. See EditCatalogItem.cshtml.cs for details.
        //services.AddScoped<ICatalogItemViewModelService, CatalogItemViewModelService>();

        // The catalog lives in its own microservice (CatalogService/). The storefront, the basket pages
        // and checkout all read product data from it over HTTP.
        services.AddHttpClient<ICatalogApiClient, CatalogApiClient>(client =>
        {
            var catalogServiceBaseUrl = configuration["CatalogServiceBaseUrl"] ?? "http://localhost:5400/";
            client.BaseAddress = new Uri(catalogServiceBaseUrl);
        });
        // Checkout (ApplicationCore's OrderCheckoutService) reads product details from CatalogService through this.
        services.AddScoped<ApplicationCore.Interfaces.ICatalogItemLookup, CatalogApiItemLookup>();

        services.AddKeyedScoped<ICatalogViewModelService, CatalogApiViewModelService>(CachedCatalogViewModelService.UncachedServiceKey);
        services.AddScoped<ICatalogViewModelService, CachedCatalogViewModelService>();

        return services;
    }
}
