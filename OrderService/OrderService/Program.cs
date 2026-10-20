using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.OrderService.Data;
using Microsoft.eShopWeb.OrderService.Endpoints;
using Microsoft.eShopWeb.OrderService.Messaging;

var builder = WebApplication.CreateBuilder(args);

var useOnlyInMemoryDatabase = builder.Configuration.GetValue<bool>("UseOnlyInMemoryDatabase");
builder.Services.AddDbContext<OrderDbContext>(options =>
{
    if (useOnlyInMemoryDatabase)
    {
        options.UseInMemoryDatabase("Orders");
    }
    else
    {
        options.UseSqlServer(builder.Configuration.GetConnectionString("OrderConnection"),
            sql => sql.EnableRetryOnFailure());
    }
});

// TODO RabbitMQ: register a RabbitMQ-backed IOrderEventPublisher here instead.
builder.Services.AddScoped<IOrderEventPublisher, LoggingOrderEventPublisher>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    if (db.Database.IsRelational())
    {
        await db.Database.MigrateAsync();
    }
}

app.UseSwagger();
app.UseSwaggerUI();

app.MapHealthChecks("/health");
app.MapOrderEndpoints();

app.Run();

// Exposed so tests can use WebApplicationFactory<Program>.
public partial class Program { }
