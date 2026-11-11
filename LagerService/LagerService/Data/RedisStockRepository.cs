using StackExchange.Redis;

namespace Microsoft.eShopWeb.LagerService.Data;

/// <summary>
/// Stores the stock in Redis (a key-value database) instead of SQL Server: the service owns its own data
/// (database per service). All items are kept in one Redis hash: key "lager:stock", field = CatalogItemId, value = quantity.
/// </summary>
public class RedisStockRepository : IStockRepository
{
    private const string StockKey = "lager:stock";
    private const string HandledEventsKey = "lager:handled-events";

    // Runs inside Redis as one step, so no other request can change the quantity between the check and the update.
    // Returns -1 if the item does not exist, -2 if there is not enough, otherwise the new quantity.
    private const string DecreaseScript = @"
local current = redis.call('HGET', KEYS[1], ARGV[1])
if not current then return -1 end
current = tonumber(current)
local amount = tonumber(ARGV[2])
if current < amount then return -2 end
return redis.call('HINCRBY', KEYS[1], ARGV[1], -amount)";

    private readonly IConnectionMultiplexer _redis;

    public RedisStockRepository(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    private IDatabase Db => _redis.GetDatabase();

    public async Task<IReadOnlyDictionary<int, int>> GetAllAsync()
    {
        var entries = await Db.HashGetAllAsync(StockKey);
        return entries.ToDictionary(e => (int)e.Name, e => (int)e.Value);
    }

    public async Task<int?> GetQuantityAsync(int catalogItemId)
    {
        var value = await Db.HashGetAsync(StockKey, catalogItemId);
        return value.HasValue ? (int)value : null;
    }

    public Task SetQuantityAsync(int catalogItemId, int quantity)
        => Db.HashSetAsync(StockKey, catalogItemId, quantity);

    public async Task<int> IncreaseAsync(int catalogItemId, int amount)
        => (int)await Db.HashIncrementAsync(StockKey, catalogItemId, amount);

    public async Task<DecreaseResult> DecreaseAsync(int catalogItemId, int amount)
    {
        var result = (int)await Db.ScriptEvaluateAsync(DecreaseScript,
            new RedisKey[] { StockKey },
            new RedisValue[] { catalogItemId, amount });

        return result switch
        {
            -1 => new DecreaseResult(DecreaseStatus.NotFound, 0),
            -2 => new DecreaseResult(DecreaseStatus.InsufficientStock, await GetQuantityAsync(catalogItemId) ?? 0),
            _ => new DecreaseResult(DecreaseStatus.Ok, result)
        };
    }

    public Task<bool> TryMarkEventHandledAsync(Guid eventId)
        => Db.SetAddAsync(HandledEventsKey, eventId.ToString());
}
