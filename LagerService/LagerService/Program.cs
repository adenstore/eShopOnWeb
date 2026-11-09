using Microsoft.eShopWeb.LagerService.Data;
using Microsoft.eShopWeb.LagerService.Endpoints;
using Microsoft.eShopWeb.LagerService.Messaging;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// The stock lives in Redis (a key-value database), not in SQL Server like the other services.
if (builder.Configuration.GetValue<bool>("UseOnlyInMemoryDatabase"))
{
    builder.Services.AddSingleton<IStockRepository, InMemoryStockRepository>();
}
else
{
    builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    {
        var options = ConfigurationOptions.Parse(builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379");
        options.AbortOnConnectFail = false; // keep retrying if Redis starts after this service
        return ConnectionMultiplexer.Connect(options);
    });
    builder.Services.AddSingleton<IStockRepository, RedisStockRepository>();
}

// OrderService publishes OrderCreated on RabbitMQ; this service listens and takes the items out of the stock.
builder.Services.Configure<RabbitMqSettings>(builder.Configuration.GetSection(RabbitMqSettings.SectionName));
builder.Services.AddScoped<OrderCreatedHandler>();
if (builder.Configuration.GetValue("RabbitMQ:Enabled", true))
{
    builder.Services.AddHostedService<OrderEventsConsumer>();
}

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapHealthChecks("/health");
app.MapStockEndpoints();

app.Run();

// Exposed so tests can use WebApplicationFactory<Program>.
public partial class Program { }
