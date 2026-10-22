using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.BasketService.Data;
using Microsoft.eShopWeb.BasketService.Data.Queries;
using Microsoft.eShopWeb.BasketService.Interfaces;
using Microsoft.eShopWeb.BasketService.Logging;

// alias
using BasketDomainService = Microsoft.eShopWeb.BasketService.Services.BasketService;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<BasketDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("BasketConnection")));

builder.Services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
builder.Services.AddScoped(typeof(IAppLogger<>), typeof(LoggerAdapter<>));
builder.Services.AddScoped<IBasketService, BasketDomainService>();
builder.Services.AddScoped<IBasketQueryService, BasketQueryService>();

// No HTTP API: the gateway reaches this service over RabbitMQ, so the basket operations
// are being re-exposed as queue consumers rather than controller actions. Until the first
// consumer lands, nothing drives the domain services registered above.
builder.Services.AddHealthChecks();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

// The one HTTP endpoint worth keeping. A message-driven service is otherwise invisible:
// without this, "container is running" cannot be told apart from "container is running but
// consuming nothing", and compose healthchecks have nothing to probe. Extend it with the
// database and the broker connection once the consumer exists.
app.MapHealthChecks("/health");

app.Logger.LogInformation("LAUNCHING BasketService");
app.Run();

public partial class Program { }
