using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.BasketService.Data;
using Microsoft.eShopWeb.BasketService.Data.Queries;
using Microsoft.eShopWeb.BasketService.Interfaces;
using Microsoft.eShopWeb.BasketService.Logging;
using Microsoft.eShopWeb.BasketService.Messaging;

// Disambiguates the domain service from the BasketService namespace it lives in.
using BasketDomainService = Microsoft.eShopWeb.BasketService.Services.BasketService;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<BasketDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("BasketConnection"),
        sql => sql.EnableRetryOnFailure()));

builder.Services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
builder.Services.AddScoped(typeof(IAppLogger<>), typeof(LoggerAdapter<>));
builder.Services.AddScoped<IBasketService, BasketDomainService>();
builder.Services.AddScoped<IBasketQueryService, BasketQueryService>();

// This service has no REST API. Its callers reach it over RabbitMQ: the gateway publishes commands
// and queries to the "Basket" topic exchange, and OrderService's "order-events" fanout tells it
// when to clear a basket. The only HTTP left is /health.
builder.Services.Configure<RabbitMqSettings>(builder.Configuration.GetSection(RabbitMqSettings.SectionName));
builder.Services.AddSingleton<IRabbitMqConnection, RabbitMqConnection>();
builder.Services.AddSingleton<IBasketEventPublisher, RabbitMqBasketEventPublisher>();

// Scoped, because it uses the scoped domain services and their DbContext. Each delivery gets its
// own scope, so one message's unit of work never leaks into the next.
builder.Services.AddScoped<BasketMessageHandler>();

builder.Services.AddHostedService<BasketCommandConsumer>();
builder.Services.AddHostedService<OrderEventsConsumer>();

builder.Services.AddHealthChecks();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.MapHealthChecks("/health");

app.Logger.LogInformation("LAUNCHING BasketService");
app.Run();

public partial class Program { }
