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
        services.AddScoped<ICatalogItemViewModelService, CatalogItemViewModelService>();
        services.Configure<CatalogSettings>(configuration);

        // The catalog is being moved into its own microservice (CatalogService/). With "UseCatalogService": true
        // the storefront reads products from it over HTTP; set it to false to fall back to the monolith's database.
        if (configuration.GetValue<bool>("UseCatalogService"))
        {
            services.AddHttpClient<ICatalogApiClient, CatalogApiClient>(client =>
            {
                var catalogServiceBaseUrl = configuration["CatalogServiceBaseUrl"] ?? "http://localhost:5400/";
                client.BaseAddress = new Uri(catalogServiceBaseUrl);
            });
            services.AddKeyedScoped<ICatalogViewModelService, CatalogApiViewModelService>(CachedCatalogViewModelService.UncachedServiceKey);
        }
        else
        {
            services.AddKeyedScoped<ICatalogViewModelService, CatalogViewModelService>(CachedCatalogViewModelService.UncachedServiceKey);
        }
        services.AddScoped<ICatalogViewModelService, CachedCatalogViewModelService>();

        return services;
    }
}
