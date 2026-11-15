using Microsoft.eShopWeb.ApiGateway.Endpoints;
using Microsoft.eShopWeb.ApiGateway.Messaging;
using Microsoft.eShopWeb.ApiGateway.Projections;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// ocelot.json routes to the docker-compose hostnames. When running locally (Development),
// ocelot.Development.json routes to the localhost ports instead. Only one of them is loaded,
// because .NET merges JSON arrays item by item, which would mix the two route lists.
var ocelotFile = $"ocelot.{builder.Environment.EnvironmentName}.json";
if (!builder.Environment.ContentRootFileProvider.GetFileInfo(ocelotFile).Exists)
{
    ocelotFile = "ocelot.json";
}
builder.Configuration.AddJsonFile(ocelotFile, optional: false, reloadOnChange: true);

builder.Services.AddOcelot(builder.Configuration);

// Basket is the one part of the API the gateway serves itself rather than proxying: BasketService
// has no REST API, so these REST calls become RabbitMQ messages here. See Endpoints/BasketEndpoints.
builder.Services.Configure<RabbitMqSettings>(builder.Configuration.GetSection(RabbitMqSettings.SectionName));
builder.Services.AddSingleton<IRabbitMqConnection, RabbitMqConnection>();
builder.Services.AddSingleton<IBasketCommandPublisher, RabbitMqBasketCommandPublisher>();
builder.Services.AddSingleton<IBasketQueryClient, RabbitMqBasketQueryClient>();

// The read side: a projection of the "basket-events" fanout, so rendering a basket — or the header
// item count, which renders on every page in the shop — is a local lookup instead of a queue
// round-trip. BasketService is still asked whenever the projection cannot answer confidently.
if (builder.Configuration.GetValue<bool>("Projection:UseOnlyInMemoryStore"))
{
    builder.Services.AddSingleton<IBasketProjection, InMemoryBasketProjection>();
}
else
{
    builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    {
        var options = ConfigurationOptions.Parse(
            builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379");
        // Keep retrying if Redis starts after the gateway, rather than throwing on first use —
        // the same reason the AMQP connection is opened lazily. A projection the gateway cannot
        // reach degrades reads to queries; it does not take the gateway down.
        options.AbortOnConnectFail = false;
        return ConnectionMultiplexer.Connect(options);
    });
    builder.Services.AddSingleton<IBasketProjection, RedisBasketProjection>();
}

// Off in tests, as in LagerService: nothing to consume from, and a background service retrying a
// broker that is not there just adds noise to the log.
if (builder.Configuration.GetValue("RabbitMQ:Enabled", true))
{
    builder.Services.AddHostedService<BasketEventsConsumer>();
}

var app = builder.Build();

// The pipeline is composed by hand because Ocelot is terminal middleware: it answers the request
// itself and never calls what comes after it, and its catch-all "/api/{everything}" route means it
// has an answer for everything. So the gateway's own endpoints have to be matched and run first,
// and Ocelot goes last as the fallback for everything they did not claim. UseEndpoints calls the
// next middleware when no endpoint matched, which is exactly that fallback.
app.UseRouting();

// ASP0014 suggests top-level registrations (app.MapPost(...)) instead. They are the wrong tool
// here: the framework appends their endpoint middleware at the very end of the pipeline, behind
// Ocelot, which never gets there.
#pragma warning disable ASP0014
app.UseEndpoints(endpoints => endpoints.MapBasketEndpoints());
#pragma warning restore ASP0014

await app.UseOcelot();

await app.RunAsync();

// Exposed so tests can use WebApplicationFactory<Program>.
public partial class Program { }
