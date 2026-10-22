using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Data.Queries;
using Microsoft.eShopWeb.Infrastructure.Logging;
using Microsoft.eShopWeb.Infrastructure.Services;

namespace Microsoft.eShopWeb.Web.Configuration;

public static class ConfigureCoreServices
{
    public static IServiceCollection AddCoreServices(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped(typeof(IReadRepository<>), typeof(EfRepository<>));
        services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));

        services.AddScoped<IOrderService, OrderCheckoutService>();

        // Orders live in their own microservice (OrderService/OrderService); the Web app calls it over HTTP.
        services.AddHttpClient<IOrderApiClient, OrderApiClient>(client =>
        {
            var orderServiceBaseUrl = configuration["OrderServiceBaseUrl"] ?? "http://localhost:5300/";
            client.BaseAddress = new Uri(orderServiceBaseUrl);
        });

        services.AddHttpClient<BasketApiClient>(client =>
        {
            var basketServiceBaseUrl = configuration["BasketServiceBaseUrl"] ?? "http://localhost:5198/";
            client.BaseAddress = new Uri(basketServiceBaseUrl);
        });
        services.AddTransient<IBasketService>(sp => sp.GetRequiredService<BasketApiClient>());
        services.AddTransient<IBasketQueryService>(sp => sp.GetRequiredService<BasketApiClient>());

        var catalogSettings = configuration.Get<CatalogSettings>() ?? new CatalogSettings();
        services.AddSingleton<IUriComposer>(new UriComposer(catalogSettings));

        services.AddScoped(typeof(IAppLogger<>), typeof(LoggerAdapter<>));
        services.AddTransient<IEmailSender, EmailSender>();

        return services;
    }
}
