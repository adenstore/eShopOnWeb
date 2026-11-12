using System.ComponentModel.DataAnnotations;
using Microsoft.eShopWeb.ApiGateway.Messaging;
using Microsoft.eShopWeb.BasketService.Contracts;

namespace Microsoft.eShopWeb.ApiGateway.Endpoints;

/// <summary>
/// The basket write routes. The gateway answers these itself instead of proxying them through
/// Ocelot, because there is no HTTP service to proxy to: BasketService's interface is RabbitMQ.
/// This is where the required architecture joins up — REST from the frontend to here, messages from
/// here inwards.
/// <para>
/// All three are one-way commands, so all three answer <c>202 Accepted</c>: the broker has the
/// command, the basket has not changed yet. None of the callers need a result — add-to-cart is
/// already Post-Redirect-Get, so the redirect re-reads the basket anyway.
/// </para>
/// </summary>
public static class BasketEndpoints
{
    public static IEndpointRouteBuilder MapBasketEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/baskets").WithTags("Basket");

        // POST /api/baskets/{buyerId}/items  { "catalogItemId": 1, "unitPrice": 9.99, "quantity": 2 }
        // buyerId is the cookie id for an anonymous shopper, the username once signed in.
        group.MapPost("/{buyerId}/items",
            (string buyerId, AddBasketItemRequest request, IBasketCommandPublisher publisher,
                ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
                AcceptAsync(BasketMessaging.RoutingKeys.AddItem, new AddItemToBasketCommand
                {
                    BuyerId = buyerId,
                    CatalogItemId = request.CatalogItemId,
                    // Priced by the caller: BasketService holds no catalog data, so it cannot look
                    // the price up. Same as the REST API this replaces.
                    UnitPrice = request.UnitPrice,
                    Quantity = request.Quantity
                }, publisher, loggerFactory, cancellationToken));

        // PUT /api/baskets/{basketId}/items  { "quantities": { "17": 3, "18": 0 } }
        // Keyed by basket *item* id; a quantity of 0 removes that item.
        group.MapPut("/{basketId:int}/items",
            (int basketId, SetBasketQuantitiesRequest request, IBasketCommandPublisher publisher,
                ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
                AcceptAsync(BasketMessaging.RoutingKeys.SetQuantities, new SetBasketQuantitiesCommand
                {
                    BasketId = basketId,
                    Quantities = request.Quantities
                }, publisher, loggerFactory, cancellationToken));

        // POST /api/baskets/transfer  { "anonymousId": "<cookie id>", "userName": "me@example.com" }
        // Moves an anonymous basket onto a username at sign-in.
        group.MapPost("/transfer",
            (TransferBasketRequest request, IBasketCommandPublisher publisher,
                ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
                AcceptAsync(BasketMessaging.RoutingKeys.Transfer, new TransferBasketCommand
                {
                    AnonymousId = request.AnonymousId,
                    UserName = request.UserName
                }, publisher, loggerFactory, cancellationToken));

        // Deliberately absent: DELETE. The basket is cleared by BasketService itself when it sees
        // OrderService's OrderCreated event, which is what removed the old
        // SetQuantities -> CreateOrder -> DeleteBasket sequence and its missing atomicity.

        return app;
    }

    /// <summary>
    /// Validates a command, publishes it, and turns the three possible outcomes into the three
    /// status codes a caller can act on: 400 for a body it must fix, 503 for a broker it should
    /// retry against, 202 for a command that is safely on the queue.
    /// </summary>
    private static async Task<IResult> AcceptAsync(string routingKey, object command,
        IBasketCommandPublisher publisher, ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!TryValidate(command, out var errors))
        {
            return Results.ValidationProblem(errors);
        }

        var logger = loggerFactory.CreateLogger(typeof(BasketEndpoints));
        try
        {
            await publisher.PublishAsync(routingKey, command, cancellationToken);
        }
        catch (Exception ex)
        {
            // Nothing was queued, so saying so is the whole point: a 202 here would tell the
            // shopper their item was added and then quietly lose it.
            logger.LogError(ex, "Could not publish {RoutingKey}; the basket command was not queued", routingKey);
            return Results.Problem(
                "The basket service cannot be reached right now. Please try again.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Accepted();
    }

    /// <summary>
    /// Runs the DataAnnotations the commands carry. Minimal APIs do not do this for you the way
    /// [ApiController] did for a controller, and validating here is the point of having a gateway in
    /// front of a queue: BasketService can only dead-letter an invalid command to the "invalid"
    /// queue with nobody left to tell, while the gateway still has a caller to answer 400 to.
    /// </summary>
    private static bool TryValidate(object command, out Dictionary<string, string[]> errors)
    {
        var results = new List<ValidationResult>();

        // validateAllProperties: true — without it only [Required] runs and every [Range] on the
        // commands is silently skipped.
        if (Validator.TryValidateObject(command, new ValidationContext(command), results,
                validateAllProperties: true))
        {
            errors = new Dictionary<string, string[]>();
            return true;
        }

        errors = results
            .SelectMany(result => result.MemberNames.DefaultIfEmpty(string.Empty),
                (result, member) => (Member: member, result.ErrorMessage))
            .GroupBy(error => error.Member, error => error.ErrorMessage ?? "Invalid value")
            .ToDictionary(group => group.Key, group => group.ToArray());
        return false;
    }
}
