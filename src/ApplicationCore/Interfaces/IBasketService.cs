using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.BasketService.Contracts;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The basket as this application can use it: BasketService owns the data, and the only way in is
/// the ApiGateway's REST surface, which publishes to RabbitMQ behind it.
/// <para>
/// The writes return <see cref="Task"/>, not a basket. They are one-way commands — the gateway
/// answers 202 once the broker has them, so there is no post-command state to hand back, and a
/// caller that needs the new state has to read it. Every page that used to render a command's
/// result is Post-Redirect-Get anyway, so the redirect does that read.
/// </para>
/// </summary>
public interface IBasketService
{
    /// <summary>
    /// The <c>BasketId</c> reported for a buyer who has no basket. Baskets are created lazily on
    /// the first add, so "no basket" is the normal state for a first-time visitor rather than an
    /// error — but 0 is not an id a command can be addressed to, and the gateway answers 400 for
    /// one that is. Callers that key off the id have to treat it as "nothing to update yet".
    /// </summary>
    const int NoBasketId = 0;

    Task TransferBasketAsync(string anonymousId, string userName);

    /// <summary>
    /// The buyer's basket, or an empty one at <see cref="NoBasketId"/> when they have none.
    /// Reading no longer creates anything, which is why this is not called GetOrCreate.
    /// </summary>
    Task<BasketSnapshot> GetBasketAsync(string buyerId);

    /// <summary>
    /// One basket by its own id, for checkout. Null when it does not exist — deliberately not
    /// get-or-create, so a missing basket cannot present itself as an empty one.
    /// </summary>
    Task<BasketSnapshot?> GetBasketAsync(int basketId);

    Task AddItemToBasket(string username, int catalogItemId, decimal price, int quantity = 1);

    /// <summary>
    /// Quantities keyed by basket <i>item</i> id; 0 removes the item. <paramref name="basketId"/>
    /// must be a real basket — see <see cref="GetBasketAsync(string)"/>.
    /// </summary>
    Task SetQuantities(int basketId, Dictionary<string, int> quantities);

    // Deliberately no DeleteBasketAsync. BasketService clears the basket itself when it sees
    // OrderService's OrderCreated event, so checkout no longer has to remember to do it — and the
    // gateway exposes no route for it.
}
