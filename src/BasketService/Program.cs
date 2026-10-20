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

var app = builder.Build();

app.Run();
