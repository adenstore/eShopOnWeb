using System.ComponentModel.DataAnnotations;
using Microsoft.eShopWeb.ApiGateway.Messaging;
using Microsoft.eShopWeb.ApiGateway.Projections;
using Microsoft.eShopWeb.BasketService.Contracts;

namespace Microsoft.eShopWeb.ApiGateway.Endpoints;

/// <summary>
/// The basket routes. The gateway answers these itself instead of proxying them through Ocelot,
/// because there is no HTTP service to proxy to: BasketService's interface is RabbitMQ. This is
/// where the required architecture joins up — REST from the frontend to here, messages from here
/// inwards.
/// <para>
/// The writes are one-way commands and answer <c>202 Accepted</c>: the broker has the command, the
/// basket has not changed yet. None of the callers need a result — add-to-cart is already
/// Post-Redirect-Get, so the redirect re-reads the basket anyway.
/// </para>
/// <para>
/// The reads come from the gateway's own projection of the "basket-events" fanout, falling back to
/// a query against BasketService when the projection cannot answer confidently. That split is the
/// point: the header item count renders on every page of the shop, so putting a broker round-trip
/// behind it would make every page view wait on a queue.
/// </para>
/// </summary>
public static class BasketEndpoints
{
    /// <summary>
    /// The id reported for a buyer who has no basket. BasketService creates baskets lazily on the
    /// first add, so "no basket" is a normal state and not an error — but a caller keying off the id
    /// has to treat 0 as "nothing to update yet" rather than as a basket it can address.
    /// </summary>
    private const int NoBasketId = 0;

    public static IEndpointRouteBuilder MapBasketEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/baskets").WithTags("Basket");

        // POST /api/baskets/{buyerId}/items  { "catalogItemId": 1, "unitPrice": 9.99, "quantity": 2 }
        // buyerId is the cookie id for an anonymous shopper, the username once signed in.
        group.MapPost("/{buyerId}/items",
            (string buyerId, AddBasketItemRequest request, IBasketCommandPublisher publisher,
                IBasketProjection projection, ILoggerFactory loggerFactory,
                CancellationToken cancellationToken) =>
                AcceptAsync(BasketMessaging.RoutingKeys.AddItem, new AddItemToBasketCommand
                {
                    BuyerId = buyerId,
                    CatalogItemId = request.CatalogItemId,
                    // Priced by the caller: BasketService holds no catalog data, so it cannot look
                    // the price up. Same as the REST API this replaces.
                    UnitPrice = request.UnitPrice,
                    Quantity = request.Quantity
                }, new[] { buyerId }, publisher, projection, loggerFactory, cancellationToken));

        // PUT /api/baskets/{basketId}/items  { "quantities": { "17": 3, "18": 0 } }
        // Keyed by basket *item* id; a quantity of 0 removes that item.
        group.MapPut("/{basketId:int}/items",
            async (int basketId, SetBasketQuantitiesRequest request, IBasketCommandPublisher publisher,
                IBasketProjection projection, ILoggerFactory loggerFactory,
                CancellationToken cancellationToken) =>
            {
                // Only the basket id reaches this route, and the projection is keyed by buyer, so
                // the owner has to be looked up to know whose reads to stop trusting. A miss here
                // is harmless: a basket the projection has never seen makes the following read
                // "unknown" anyway, which sends it to BasketService regardless.
                var owner = await projection.OwnerOfAsync(basketId, cancellationToken);

                return await AcceptAsync(BasketMessaging.RoutingKeys.SetQuantities,
                    new SetBasketQuantitiesCommand
                    {
                        BasketId = basketId,
                        Quantities = request.Quantities
                    },
                    owner is null ? Array.Empty<string>() : new[] { owner },
                    publisher, projection, loggerFactory, cancellationToken);
            });

        // POST /api/baskets/transfer  { "anonymousId": "<cookie id>", "userName": "me@example.com" }
        // Moves an anonymous basket onto a username at sign-in.
        group.MapPost("/transfer",
            (TransferBasketRequest request, IBasketCommandPublisher publisher,
                IBasketProjection projection, ILoggerFactory loggerFactory,
                CancellationToken cancellationToken) =>
                AcceptAsync(BasketMessaging.RoutingKeys.Transfer, new TransferBasketCommand
                {
                    AnonymousId = request.AnonymousId,
                    UserName = request.UserName
                },
                // Two buyers change, and BasketService publishes an event for each: the username
                // absorbs the items and the cookie id is emptied. Guarding only one of them would
                // leave the other's projection trusted while its event is still in flight.
                new[] { request.AnonymousId, request.UserName },
                publisher, projection, loggerFactory, cancellationToken));

        // Deliberately absent: DELETE. The basket is cleared by BasketService itself when it sees
        // OrderService's OrderCreated event, which is what removed the old
        // SetQuantities -> CreateOrder -> DeleteBasket sequence and its missing atomicity.

        // GET /api/baskets/{buyerId}  ->  the whole basket, for rendering the basket page.
        // Answers an empty basket with id 0 rather than 404 when the buyer has none: baskets are
        // created on the first add, so for every first-time visitor "no basket" is the normal case
        // and a 404 would make the common path an error path.
        group.MapGet("/{buyerId}",
            async (string buyerId, IBasketProjection projection, IBasketQueryClient queries,
                ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
            {
                var (basket, failure) = await ReadByBuyerAsync(buyerId, projection, queries,
                    loggerFactory, cancellationToken);

                return failure ?? Results.Ok(basket);
            });

        // GET /api/baskets/{buyerId}/items/count  ->  { "count": 3 }
        // The hottest basket route in the shop: it is a ViewComponent in the shared layout, so it
        // runs on every page render. Same read as the route above, so the header count can never
        // disagree with the basket page rendered beside it.
        group.MapGet("/{buyerId}/items/count",
            async (string buyerId, IBasketProjection projection, IBasketQueryClient queries,
                ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
            {
                var (basket, failure) = await ReadByBuyerAsync(buyerId, projection, queries,
                    loggerFactory, cancellationToken);

                return failure ?? Results.Ok(new BasketItemCountResponse(basket!.TotalItems));
            });

        // GET /api/baskets/{basketId}  ->  one basket by its own id, for checkout.
        // The int constraint is what separates this from the by-buyer route above; endpoint routing
        // prefers the constrained segment, so the two never collide. It does mean a buyer id that
        // is nothing but digits would land here, which cannot happen in this shop — a buyer id is
        // either a cookie GUID or an email address.
        group.MapGet("/{basketId:int}",
            async (int basketId, IBasketQueryClient queries, ILoggerFactory loggerFactory,
                CancellationToken cancellationToken) =>
            {
                var logger = loggerFactory.CreateLogger(typeof(BasketEndpoints));

                BasketQueryReply reply;
                try
                {
                    // Never from the projection, however fresh it looks. This is the read checkout
                    // builds an order from, where stale items or stale prices are a money bug
                    // rather than a cosmetic one — it is the one read worth a broker round-trip,
                    // and it happens once per order rather than once per page.
                    reply = await queries.GetByIdAsync(basketId, cancellationToken);
                }
                catch (Exception ex)
                {
                    return CouldNotAsk(ex, logger, $"basket {basketId}");
                }

                if (reply.Error is not null)
                {
                    return UpstreamError(reply.Error, logger);
                }

                // 404 here, unlike the by-buyer route: asking for a specific basket that does not
                // exist is a real miss, and answering an empty basket instead would sail past the
                // caller's null check and turn it into "the order has no items".
                return reply.Basket is null ? Results.NotFound() : Results.Ok(reply.Basket);
            });

        return app;
    }

    /// <summary>
    /// A buyer's basket, from the projection if it can answer and from BasketService if it cannot.
    /// Returns the basket, or the <see cref="IResult"/> to answer with when it could not be read at
    /// all — never both.
    /// </summary>
    private static async Task<(BasketSnapshot? Basket, IResult? Failure)> ReadByBuyerAsync(
        string buyerId, IBasketProjection projection, IBasketQueryClient queries,
        ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        var projected = await projection.ReadAsync(buyerId, cancellationToken);
        switch (projected.Kind)
        {
            case ProjectedBasketKind.Basket:
                return (projected.Basket, null);

            case ProjectedBasketKind.NoBasket:
                return (BasketSnapshot.Empty(NoBasketId, buyerId), null);
        }

        // Unknown. Either nothing is projected for this buyer yet, or one of their own writes is
        // still in flight and the projection is deliberately refusing to answer. Asking the service
        // is correct in both cases: commands and queries share one FIFO queue with a single
        // consumer, so a query published after a command is handled after it.
        var logger = loggerFactory.CreateLogger(typeof(BasketEndpoints));

        BasketQueryReply reply;
        try
        {
            reply = await queries.GetByBuyerAsync(buyerId, cancellationToken);
        }
        catch (Exception ex)
        {
            // Not answered from the projection as a consolation prize. It declined for a reason —
            // most often that this shopper has just changed their basket — and showing them their
            // previous basket as though it were current is the "I added it and it vanished" bug the
            // in-flight guard exists to prevent.
            return (null, CouldNotAsk(ex, logger, $"buyer {buyerId}"));
        }

        if (reply.Error is not null)
        {
            return (null, UpstreamError(reply.Error, logger));
        }

        if (reply.Basket is null)
        {
            // Remember the absence, or every page view by a shopper who has not added anything yet
            // — the commonest request in the shop — would cost a broker round-trip.
            await projection.RememberNoBasketAsync(buyerId, cancellationToken);
            return (BasketSnapshot.Empty(NoBasketId, buyerId), null);
        }

        return (reply.Basket, null);
    }

    /// <summary>
    /// 504 when BasketService did not answer in time, 503 when the question could not be asked at
    /// all. Both are retryable and neither is the caller's fault, but they are different problems:
    /// a timeout means the service is behind, a 503 means the broker is unreachable.
    /// </summary>
    private static IResult CouldNotAsk(Exception exception, ILogger logger, string what)
    {
        if (exception is TimeoutException)
        {
            logger.LogWarning(exception, "BasketService did not answer for {What} in time", what);
            return Results.Problem(
                "The basket service is not responding right now. Please try again.",
                statusCode: StatusCodes.Status504GatewayTimeout);
        }

        logger.LogError(exception, "Could not ask BasketService for {What}", what);
        return Results.Problem(
            "The basket service cannot be reached right now. Please try again.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    /// <summary>
    /// BasketService answered, but with an error rather than a basket. 502: the failure is upstream
    /// of here and there is nothing the caller can fix.
    /// </summary>
    private static IResult UpstreamError(string error, ILogger logger)
    {
        logger.LogError("BasketService rejected a basket query: {Error}", error);
        return Results.Problem("The basket could not be read.", statusCode: StatusCodes.Status502BadGateway);
    }

    /// <summary>
    /// Validates a command, publishes it, and turns the three possible outcomes into the three
    /// status codes a caller can act on: 400 for a body it must fix, 503 for a broker it should
    /// retry against, 202 for a command that is safely on the queue.
    /// </summary>
    private static async Task<IResult> AcceptAsync(string routingKey, object command,
        IReadOnlyCollection<string> buyers, IBasketCommandPublisher publisher,
        IBasketProjection projection, ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!TryValidate(command, out var errors))
        {
            return Results.ValidationProblem(errors);
        }

        var logger = loggerFactory.CreateLogger(typeof(BasketEndpoints));

        // Marked before the publish, not after. Marking first can only over-count — a publish that
        // then fails leaves the buyer served from BasketService until the mark expires, which is
        // slower but still right. Marking afterwards risks the opposite: the event for this command
        // can come back and be projected before the mark is written, and then the mark blocks reads
        // of a projection that is already fresh, or worse, a crash in between leaves the read
        // trusting a projection that is not.
        foreach (var buyer in buyers)
        {
            await projection.NoteWriteInFlightAsync(buyer, cancellationToken);
        }

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
