using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.OrderService.Contracts;
using Microsoft.eShopWeb.OrderService.Contracts.Events;
using Microsoft.eShopWeb.OrderService.Data;
using Microsoft.eShopWeb.OrderService.Messaging;

namespace Microsoft.eShopWeb.OrderService.Endpoints;

public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/api/orders").WithTags("Orders");

        orders.MapPost("/", CreateOrder);
        orders.MapGet("/", GetOrdersForBuyer);
        orders.MapGet("/{orderId:int}", GetOrderById);

        return app;
    }

    public static async Task<Results<Created<CreateOrderResponse>, ValidationProblem>> CreateOrder(
        CreateOrderRequest request,
        OrderDbContext db,
        IOrderEventPublisher publisher,
        CancellationToken cancellationToken)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var order = request.ToOrder();
        db.Orders.Add(order);
        await db.SaveChangesAsync(cancellationToken);

        var dto = order.ToDto();
        await publisher.PublishAsync(new OrderCreatedIntegrationEvent(
            Guid.NewGuid(), DateTimeOffset.UtcNow, dto.Id, dto.BuyerId, dto.Total, dto.Items), cancellationToken);

        return TypedResults.Created($"/api/orders/{order.Id}", new CreateOrderResponse(order.Id));
    }

    public static async Task<Ok<List<OrderDto>>> GetOrdersForBuyer(
        string buyerId,
        OrderDbContext db,
        CancellationToken cancellationToken)
    {
        var orders = await db.Orders
            .AsNoTracking()
            .Include(o => o.OrderItems)
            .Where(o => o.BuyerId == buyerId)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(orders.Select(o => o.ToDto()).ToList());
    }

    public static async Task<Results<Ok<OrderDto>, NotFound>> GetOrderById(
        int orderId,
        string buyerId,
        OrderDbContext db,
        CancellationToken cancellationToken)
    {
        // Filtering on buyerId means a buyer can only ever see their own orders.
        var order = await db.Orders
            .AsNoTracking()
            .Include(o => o.OrderItems)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.BuyerId == buyerId, cancellationToken);

        return order is null ? TypedResults.NotFound() : TypedResults.Ok(order.ToDto());
    }

    private static Dictionary<string, string[]> Validate(CreateOrderRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.BuyerId))
        {
            errors[nameof(request.BuyerId)] = new[] { "BuyerId is required." };
        }
        if (request.ShipToAddress is null)
        {
            errors[nameof(request.ShipToAddress)] = new[] { "ShipToAddress is required." };
        }
        if (request.Items is null || request.Items.Count == 0)
        {
            errors[nameof(request.Items)] = new[] { "An order must contain at least one item." };
        }
        else if (request.Items.Any(i => i.Units <= 0 || i.UnitPrice < 0 || i.CatalogItemId <= 0
                                        || string.IsNullOrWhiteSpace(i.ProductName) || string.IsNullOrWhiteSpace(i.PictureUri)))
        {
            errors[nameof(request.Items)] = new[] { "Every item needs a catalog item id, name, picture, a positive quantity and a non-negative price." };
        }
        return errors;
    }
}
