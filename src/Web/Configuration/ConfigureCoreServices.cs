using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Logging;
using Microsoft.eShopWeb.Infrastructure.Services;

namespace Microsoft.eShopWeb.Web.Configuration;

public static class ConfigureCoreServices
{
    public static IServiceCollection AddCoreServices(this IServiceCollection services,
        IConfiguration configuration)
    {
        // Nothing resolves these any more — the basket was the monolith's last entity, and
        // CatalogContext is now empty. Left registered with the context itself; see the remark on
        // CatalogContext for what has to be true before the whole EF layer can go.
        services.AddScoped(typeof(IReadRepository<>), typeof(EfRepository<>));
        services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));

        services.AddScoped<IOrderService, OrderCheckoutService>();

        // Orders live in their own microservice (OrderService/OrderService); the Web app calls it over HTTP through the ApiGateway.
        services.AddHttpClient<IOrderApiClient, OrderApiClient>(client =>
        {
            var orderServiceBaseUrl = configuration["OrderServiceBaseUrl"] ?? "http://localhost:5278/";
            client.BaseAddress = new Uri(orderServiceBaseUrl);
        });

        // The basket lives in BasketService (BasketService/BasketService), whose only interface is
        // RabbitMQ. The Web app is the frontend, so it keeps speaking REST: BasketServiceBaseUrl
        // points at the ApiGateway, which publishes the commands and answers the reads.
        //
        // BasketApiClient implements both interfaces, and AddHttpClient<TInterface, TImpl> only
        // registers the one it is given — so the second resolves the same instance rather than
        // being a second typed client, which would build a second HttpClient and a second
        // BasketApiClient per request.
        services.AddHttpClient<IBasketService, BasketApiClient>(client =>
        {
            var basketServiceBaseUrl = configuration["BasketServiceBaseUrl"] ?? "http://localhost:5278/";
            client.BaseAddress = new Uri(basketServiceBaseUrl);
        });
        services.AddScoped<IBasketQueryService>(sp => (BasketApiClient)sp.GetRequiredService<IBasketService>());

        services.AddScoped(typeof(IAppLogger<>), typeof(LoggerAdapter<>));
        services.AddTransient<IEmailSender, EmailSender>();

        return services;
    }
}
