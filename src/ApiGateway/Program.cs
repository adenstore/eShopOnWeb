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

var app = builder.Build();

await app.UseOcelot();

await app.RunAsync();
