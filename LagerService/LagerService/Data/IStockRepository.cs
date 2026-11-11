namespace Microsoft.eShopWeb.LagerService.Data;

/// <summary>
/// The stock of each catalog item: CatalogItemId (from CatalogService) -> quantity on hand.
/// <see cref="RedisStockRepository"/> is used when the service runs; the tests use <see cref="InMemoryStockRepository"/>.
/// </summary>
public interface IStockRepository
{
    Task<IReadOnlyDictionary<int, int>> GetAllAsync();

    /// <returns>The quantity, or null if the item is not in the stock.</returns>
    Task<int?> GetQuantityAsync(int catalogItemId);

    Task SetQuantityAsync(int catalogItemId, int quantity);

    /// <summary>Adds <paramref name="amount"/> to the stock (creates the item if needed).</summary>
    /// <returns>The new quantity.</returns>
    Task<int> IncreaseAsync(int catalogItemId, int amount);

    /// <summary>
    /// Removes <paramref name="amount"/> from the stock as one atomic operation, so two orders
    /// placed at the same time cannot both take the last item. Nothing changes if there is too little.
    /// </summary>
    Task<DecreaseResult> DecreaseAsync(int catalogItemId, int amount);

    /// <summary>Remembers that an event has been handled.</summary>
    /// <returns>false if it was already handled before (RabbitMQ delivered it twice).</returns>
    Task<bool> TryMarkEventHandledAsync(Guid eventId);
}

public enum DecreaseStatus
{
    Ok,
    NotFound,
    InsufficientStock
}

public record DecreaseResult(DecreaseStatus Status, int Quantity);
