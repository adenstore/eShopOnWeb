using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.ApiGateway.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Microsoft.eShopWeb.ApiGateway.Tests;

/// <summary>
/// Runs the gateway in memory with the broker replaced by
/// <see cref="RecordingBasketCommandPublisher"/>. Ocelot still starts and still owns the rest of
/// the pipeline, which is the part worth having a real host for: its catch-all route matches
/// everything, so these tests fail if the basket endpoints end up behind it.
/// </summary>
public class ApiGatewayApplication : WebApplicationFactory<Program>
{
    public RecordingBasketCommandPublisher Publisher { get; } = new();

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
        });

        return base.CreateHost(builder);
    }
}
