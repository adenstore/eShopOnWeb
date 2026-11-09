using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.BasketService.Contracts;

/// <summary>
/// The write side: one-way commands, published to <see cref="BasketMessaging.CommandExchange"/>,
/// no reply. None of the callers use a return value — add-to-cart is already Post-Redirect-Get,
/// so the redirect discards whatever the old REST call returned.
/// <para>
/// The DataAnnotations below are enforced by hand. Under [ApiController] an invalid body was
/// rejected with a ProblemDetails 400 before the action ran, which is why the domain services hold
/// no guard clauses for these values. A queue consumer gets no such treatment — nothing invokes
/// DataAnnotations for you — so <c>MessageValidator</c> runs them explicitly and a failure
/// dead-letters the message to <see cref="BasketMessaging.InvalidQueue"/>. There is no caller to
/// return a 400 to.
/// </para>
/// </summary>
public record AddItemToBasketCommand
{
    /// <summary>Cookie id for an anonymous shopper, username once signed in.</summary>
    [Required]
    public string BuyerId { get; init; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int CatalogItemId { get; init; }

    /// <summary>
    /// Priced by the caller, not looked up here — BasketService holds no catalog data. Same
    /// behaviour the REST API had.
    /// </summary>
    [Range(0.0, double.MaxValue)]
    public decimal UnitPrice { get; init; }

    [Range(1, int.MaxValue)]
    public int Quantity { get; init; } = 1;
}

public record SetBasketQuantitiesCommand
{
    [Range(1, int.MaxValue)]
    public int BasketId { get; init; }

    /// <summary>
    /// Keyed by basket *item* id as a string — the shape
    /// <see cref="Interfaces.IBasketService.SetQuantities"/> already expects. An empty map is
    /// valid and no-ops; a quantity of 0 removes the item.
    /// </summary>
    [Required]
    public Dictionary<string, int> Quantities { get; init; } = new();
}

/// <summary>
/// Moves an anonymous basket onto a username at sign-in. The anonymous basket is deleted, so this
/// is safe to redeliver only in the sense that a repeat finds nothing to move and no-ops.
/// </summary>
public record TransferBasketCommand
{
    [Required]
    public string AnonymousId { get; init; } = string.Empty;

    [Required]
    public string UserName { get; init; } = string.Empty;
}
