using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.BasketService.Data;
using Microsoft.eShopWeb.BasketService.Data.Queries;
using Microsoft.eShopWeb.BasketService.Interfaces;
using Microsoft.eShopWeb.BasketService.Logging;
using Microsoft.OpenApi.Models;

// alias
using BasketDomainService = Microsoft.eShopWeb.BasketService.Services.BasketService;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<BasketDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("BasketConnection")));

builder.Services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
builder.Services.AddScoped(typeof(IAppLogger<>), typeof(LoggerAdapter<>));
builder.Services.AddScoped<IBasketService, BasketDomainService>();
builder.Services.AddScoped<IBasketQueryService, BasketQueryService>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Basket Service", Version = "v1" });
    c.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, "BasketService.xml"), true);
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Basket Service v1"));
}

app.MapControllers();

app.Logger.LogInformation("LAUNCHING BasketService");
app.Run();

public partial class Program { }
