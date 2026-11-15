using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.ApiGateway.Messaging;
using Microsoft.eShopWeb.ApiGateway.Projections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace Microsoft.eShopWeb.ApiGateway.Tests;

/// <summary>
/// Runs the gateway in memory with the broker replaced: commands are recorded by
/// <see cref="RecordingBasketCommandPublisher"/> and queries answered by
/// <see cref="FakeBasketQueryClient"/>. Ocelot still starts and still owns the rest of the
/// pipeline, which is the part worth having a real host for: its catch-all route matches
/// everything, so these tests fail if the basket endpoints end up behind it.
/// <para>
/// The projection is the real <see cref="InMemoryBasketProjection"/> rather than a stub, so the
/// rules the read endpoints are built on — newer-wins, the in-flight guard, the cached absence —
/// are the ones actually under test. Only its backing store differs from a deployed gateway's.
/// </para>
/// </summary>
public class ApiGatewayApplication : WebApplicationFactory<Program>
{
    public RecordingBasketCommandPublisher Publisher { get; } = new();

    public FakeBasketQueryClient Queries { get; } = new();

    public InMemoryBasketProjection Projection { get; } = new();

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Development, so the gateway loads ocelot.Development.json and its proxied routes point at
        // localhost ports rather than compose hostnames. Nothing is listening on them in a test;
        // only the basket routes, which are not proxied at all, are exercised here.
        builder.UseEnvironment("Development");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IBasketCommandPublisher>();
            services.AddSingleton<IBasketCommandPublisher>(Publisher);

            services.RemoveAll<IBasketQueryClient>();
            services.AddSingleton<IBasketQueryClient>(Queries);

            services.RemoveAll<IBasketProjection>();
            services.AddSingleton<IBasketProjection>(Projection);

            // Removed rather than left unresolved: nothing should be able to reach for Redis from a
            // test, and a registration that would try is a trap for whoever adds the next service.
            services.RemoveAll<IConnectionMultiplexer>();

            // There is no broker to consume basket events from. Removed by implementation type
            // rather than with RemoveAll<IHostedService>, which would also take out Ocelot's.
            var consumer = services.FirstOrDefault(service =>
                service.ImplementationType == typeof(BasketEventsConsumer));
            if (consumer is not null)
            {
                services.Remove(consumer);
            }
        });

        return base.CreateHost(builder);
    }
}
