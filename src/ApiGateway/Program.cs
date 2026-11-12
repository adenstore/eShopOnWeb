using Microsoft.eShopWeb.ApiGateway.Endpoints;
using Microsoft.eShopWeb.ApiGateway.Messaging;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;

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
