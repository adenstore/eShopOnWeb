using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.CatalogService.Api;
using Microsoft.eShopWeb.CatalogService.Api.Authorization;
using Microsoft.eShopWeb.CatalogService.Api.Middleware;
using Microsoft.eShopWeb.CatalogService.Domain;
using Microsoft.eShopWeb.CatalogService.Domain.Interfaces;
using Microsoft.eShopWeb.CatalogService.Domain.Services;
using Microsoft.eShopWeb.CatalogService.Infrastructure.Data;
using Microsoft.IdentityModel.Tokens;
using MinimalApi.Endpoint.Configurations.Extensions;
using MinimalApi.Endpoint.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpoints();

var useOnlyInMemoryDatabase = builder.Configuration.GetValue<bool>("UseOnlyInMemoryDatabase");
builder.Services.AddDbContext<CatalogContext>(options =>
{
    if (useOnlyInMemoryDatabase)
    {
        options.UseInMemoryDatabase("Catalog");
    }
    else
    {
        options.UseSqlServer(builder.Configuration.GetConnectionString("CatalogConnection"),
            sql => sql.EnableRetryOnFailure());
    }
});

builder.Services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
builder.Services.AddScoped(typeof(IReadRepository<>), typeof(EfRepository<>));
var catalogSettings = builder.Configuration.Get<CatalogSettings>() ?? new CatalogSettings();
builder.Services.AddSingleton<IUriComposer>(new UriComposer(catalogSettings));
builder.Services.AddAutoMapper(typeof(MappingProfile).Assembly);

// Validates the same JWTs the monolith's PublicApi issues, so admin calls keep working.
var key = Encoding.ASCII.GetBytes(AuthorizationConstants.JWT_SECRET_KEY);
builder.Services.AddAuthentication(config =>
{
    config.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(config =>
{
    config.RequireHttpsMetadata = false;
    config.SaveToken = true;
    config.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateIssuer = false,
        ValidateAudience = false
    };
});
builder.Services.AddAuthorization();

// BlazorAdmin runs in the browser on the Web app's address and calls this service through the gateway.
const string CORS_POLICY = "CorsPolicy";
var webBase = builder.Configuration["baseUrls:webBase"] ?? "https://localhost:5001/";
builder.Services.AddCors(options =>
{
    options.AddPolicy(name: CORS_POLICY,
        corsPolicyBuilder =>
        {
            corsPolicyBuilder.WithOrigins(webBase.Replace("host.docker.internal", "localhost").TrimEnd('/'));
            corsPolicyBuilder.AllowAnyMethod();
            corsPolicyBuilder.AllowAnyHeader();
        });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var catalogContext = scope.ServiceProvider.GetRequiredService<CatalogContext>();
    await CatalogContextSeed.SeedAsync(catalogContext, app.Logger);
}

app.UseMiddleware<ExceptionMiddleware>();

app.UseCors(CORS_POLICY);

app.UseAuthentication();
app.UseAuthorization();

app.UseSwagger();
app.UseSwaggerUI();

app.MapHealthChecks("/health");
app.MapEndpoints();

app.Run();

// Exposed so tests can use WebApplicationFactory<Program>.
public partial class Program { }
