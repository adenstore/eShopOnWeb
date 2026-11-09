namespace Microsoft.eShopWeb.LagerService.Data;

/// <summary>
/// Keeps the stock in memory. Used by the tests and when "UseOnlyInMemoryDatabase" is true, so no Redis is needed.
/// </summary>
public class InMemoryStockRepository : IStockRepository
{
    private readonly Dictionary<int, int> _stock = new();
    private readonly HashSet<Guid> _handledEvents = new();
    private readonly object _lock = new();

    public Task<IReadOnlyDictionary<int, int>> GetAllAsync()
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyDictionary<int, int>>(new Dictionary<int, int>(_stock));
        }
    }

    public Task<int?> GetQuantityAsync(int catalogItemId)
    {
        lock (_lock)
        {
            return Task.FromResult(_stock.TryGetValue(catalogItemId, out var quantity) ? quantity : (int?)null);
        }
    }

    public Task SetQuantityAsync(int catalogItemId, int quantity)
    {
        lock (_lock)
        {
            _stock[catalogItemId] = quantity;
        }
        return Task.CompletedTask;
    }

    public Task<int> IncreaseAsync(int catalogItemId, int amount)
    {
        lock (_lock)
        {
            _stock[catalogItemId] = _stock.GetValueOrDefault(catalogItemId) + amount;
            return Task.FromResult(_stock[catalogItemId]);
        }
    }

    public Task<DecreaseResult> DecreaseAsync(int catalogItemId, int amount)
    {
        lock (_lock)
        {
            if (!_stock.TryGetValue(catalogItemId, out var current))
            {
                return Task.FromResult(new DecreaseResult(DecreaseStatus.NotFound, 0));
            }
            if (current < amount)
            {
                return Task.FromResult(new DecreaseResult(DecreaseStatus.InsufficientStock, current));
            }
            _stock[catalogItemId] = current - amount;
            return Task.FromResult(new DecreaseResult(DecreaseStatus.Ok, current - amount));
        }
    }

    public Task<bool> TryMarkEventHandledAsync(Guid eventId)
    {
        lock (_lock)
        {
            return Task.FromResult(_handledEvents.Add(eventId));
        }
    }
}
