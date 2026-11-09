using Microsoft.eShopWeb.LagerService.Data;

namespace Microsoft.eShopWeb.LagerService.Endpoints;

public static class StockEndpoints
{
    public static IEndpointRouteBuilder MapStockEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/stock").WithTags("Stock");

        // GET /api/stock -> the whole stock
        group.MapGet("/", async (IStockRepository stock) =>
        {
            var all = await stock.GetAllAsync();
            return Results.Ok(all.OrderBy(s => s.Key).Select(s => new StockItemDto(s.Key, s.Value)));
        });

        // GET /api/stock/5 -> how many of catalog item 5 are in stock
        group.MapGet("/{catalogItemId:int}", async (int catalogItemId, IStockRepository stock) =>
        {
            var quantity = await stock.GetQuantityAsync(catalogItemId);
            return quantity is null
                ? Results.NotFound()
                : Results.Ok(new StockItemDto(catalogItemId, quantity.Value));
        });

        // PUT /api/stock/5 { "quantity": 20 } -> set the quantity (e.g. a new item, or after counting the stock)
        group.MapPut("/{catalogItemId:int}", async (int catalogItemId, SetQuantityRequest request, IStockRepository stock) =>
        {
            if (request.Quantity < 0)
            {
                return Results.BadRequest("Quantity cannot be negative.");
            }
            await stock.SetQuantityAsync(catalogItemId, request.Quantity);
            return Results.Ok(new StockItemDto(catalogItemId, request.Quantity));
        });

        // POST /api/stock/5/increase { "amount": 10 } -> goods received
        group.MapPost("/{catalogItemId:int}/increase", async (int catalogItemId, ChangeQuantityRequest request, IStockRepository stock) =>
        {
            if (request.Amount <= 0)
            {
                return Results.BadRequest("Amount must be greater than 0.");
            }
            var quantity = await stock.IncreaseAsync(catalogItemId, request.Amount);
            return Results.Ok(new StockItemDto(catalogItemId, quantity));
        });

        // POST /api/stock/5/decrease { "amount": 2 } -> goods sold. Orders normally do this through RabbitMQ.
        group.MapPost("/{catalogItemId:int}/decrease", async (int catalogItemId, ChangeQuantityRequest request, IStockRepository stock) =>
        {
            if (request.Amount <= 0)
            {
                return Results.BadRequest("Amount must be greater than 0.");
            }
            var result = await stock.DecreaseAsync(catalogItemId, request.Amount);
            return result.Status switch
            {
                DecreaseStatus.NotFound => Results.NotFound(),
                DecreaseStatus.InsufficientStock => Results.Conflict(new StockItemDto(catalogItemId, result.Quantity)),
                _ => Results.Ok(new StockItemDto(catalogItemId, result.Quantity))
            };
        });

        return app;
    }
}
