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

        // Basket runs in-process again while BasketService's transport is rebuilt on RabbitMQ.
        // Its REST API is being deleted, so there is nothing left for BasketApiClient to call.
        // The client and the BasketServiceBaseUrl config key are kept but unregistered: the Web
        // app is the frontend, so it keeps speaking REST once the gateway is in place — it will
        // point at the gateway rather than at the service, and the gateway does the publishing.
        services.AddScoped<IBasketService, ApplicationCore.Services.BasketService>();
        services.AddScoped<IBasketQueryService, Infrastructure.Data.Queries.BasketQueryService>();

        var catalogSettings = configuration.Get<CatalogSettings>() ?? new CatalogSettings();
        services.AddSingleton<IUriComposer>(new UriComposer(catalogSettings));

        services.AddScoped(typeof(IAppLogger<>), typeof(LoggerAdapter<>));
        services.AddTransient<IEmailSender, EmailSender>();

        return services;
    }
}
