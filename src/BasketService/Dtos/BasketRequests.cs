using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.BasketService.Dtos;

/// <summary>
/// Request bodies for <see cref="Controllers.BasketController"/>. Validation lives on the
/// attributes here — [ApiController] rejects an invalid body with a ProblemDetails 400
/// before the action runs, so the actions themselves hold no guard clauses.
/// </summary>
public record AddBasketItemRequest
{
    [Range(1, int.MaxValue)]
    public int CatalogItemId { get; init; }

    [Range(0.0, double.MaxValue)]
    public decimal UnitPrice { get; init; }

    [Range(1, int.MaxValue)]
    public int Quantity { get; init; } = 1;
}

public record SetBasketQuantitiesRequest
{
    /// <summary>
    /// Keyed by basket *item* id as a string — the shape
    /// <see cref="Interfaces.IBasketService.SetQuantities"/> already expects, kept as-is so
    /// the monolith's facade can map onto it one-to-one. An empty map is valid and no-ops.
    /// </summary>
    [Required]
    public Dictionary<string, int> Quantities { get; init; } = new();
}

public record TransferBasketRequest
{
    [Required]
    public string AnonymousId { get; init; } = string.Empty;

    [Required]
    public string UserName { get; init; } = string.Empty;
}
