using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.BasketService.Dtos;

/// <summary>
/// Command shapes for the basket operations. Unreferenced since the REST controller was
/// removed — they are kept as the starting point for the RabbitMQ message contracts, which
/// want these same shapes.
/// <para>
/// WARNING: the validation attributes below are currently inert. They worked because
/// [ApiController] rejected an invalid body with a ProblemDetails 400 before the action ran,
/// which is why the domain services hold no guard clauses for these values. A queue consumer
/// gets no such treatment: nothing invokes DataAnnotations for you, so a malformed message
/// would reach the domain unchecked. Whoever writes the consumer must either call
/// Validator.TryValidateObject explicitly or add real guard clauses — and decide what a
/// failure means on a message, which cannot be a 400 to anyone (dead-letter, or an error
/// field in the reply).
/// </para>
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
