using System.Text.Json;
using Microsoft.eShopWeb.BasketService.Contracts;
using StackExchange.Redis;

namespace Microsoft.eShopWeb.ApiGateway.Projections;

/// <summary>
/// The projection in Redis, so it survives a gateway restart and is shared by every gateway
/// instance. Redis rather than SQL Server because this is a cache of someone else's data keyed by
/// one string, which is the shape Redis is for — and because the gateway owning a relational
/// database for data BasketService owns would be the database-per-service violation the whole
/// extraction is about avoiding. LagerService already keeps its stock here.
/// </summary>
/// <remarks>
/// Each buyer's entry is a hash with two fields: <c>at</c>, the event's
/// <see cref="BasketChangedIntegrationEvent.OccurredAt"/> in Unix milliseconds, and <c>basket</c>,
/// the serialized snapshot (empty for "this buyer has no basket"). The timestamp is stored next to
/// the snapshot because the apply has to compare it, and comparing it inside Redis is what makes
/// the apply atomic.
/// </remarks>
public sealed class RedisBasketProjection : IBasketProjection
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Newer-wins apply. Within one basket events are published in order, but a redelivery after a
    /// broker or consumer restart can arrive behind a newer one, and overwriting unconditionally
    /// would resurrect items the shopper has already removed. Comparing and writing in one script
    /// keeps that check atomic across gateway instances, which share the queue and this Redis.
    /// <para>
    /// The clamped decrement of the in-flight count is here rather than in a second round-trip for
    /// the same reason: a read that lands between the two would see a fresh projection and a buyer
    /// still marked in flight, and send a query nobody needed.
    /// </para>
    /// </summary>
    private const string ApplyScript = """
        local stored = redis.call('HGET', KEYS[1], 'at')
        if stored and tonumber(stored) > tonumber(ARGV[1]) then
          return 0
        end
        redis.call('HSET', KEYS[1], 'at', ARGV[1], 'basket', ARGV[2])
        redis.call('EXPIRE', KEYS[1], ARGV[3])
        redis.call('SET', KEYS[3], ARGV[4], 'EX', ARGV[3])
        if tonumber(redis.call('GET', KEYS[2]) or '0') > 0 then
          redis.call('DECR', KEYS[2])
        end
        return 1
        """;

    /// <summary>
    /// Caches an absence only when there is nothing there at all. A query reply carries no event
    /// timestamp, so it cannot be ordered against the events on the fanout — which means it must
    /// never be allowed to overwrite one. "Only if absent" is that rule, and it needs to be one
    /// step so an event arriving concurrently cannot be clobbered.
    /// </summary>
    private const string RememberNoBasketScript = """
        if redis.call('EXISTS', KEYS[1]) == 1 then
          return 0
        end
        redis.call('HSET', KEYS[1], 'at', '0', 'basket', '')
        redis.call('EXPIRE', KEYS[1], ARGV[1])
        return 1
        """;

    /// <summary>
    /// Increment and expire together: done as two calls, a crash in between would leave a counter
    /// with no expiry, and reads for that buyer would be served from BasketService forever.
    /// </summary>
    private const string NoteInFlightScript = """
        local n = redis.call('INCR', KEYS[1])
        redis.call('EXPIRE', KEYS[1], ARGV[1])
        return n
        """;

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisBasketProjection> _logger;

    public RedisBasketProjection(IConnectionMultiplexer redis, ILogger<RedisBasketProjection> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    private IDatabase Db => _redis.GetDatabase();

    private static RedisKey BasketKey(string buyerId) => $"basket:{buyerId}";

    private static RedisKey InFlightKey(string buyerId) => $"basket:pending:{buyerId}";

    private static RedisKey OwnerKey(int basketId) => $"basket:owner:{basketId}";

    public async Task<ProjectedBasket> ReadAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        try
        {
            // The in-flight count is read first and the entry second, so a write landing between the
            // two can only make the answer more cautious: the entry may be newer than the count
            // suggests, never older.
            var inFlight = (long?)await Db.StringGetAsync(InFlightKey(buyerId)) ?? 0;
            if (inFlight > 0)
            {
                return ProjectedBasket.Unknown;
            }

            var entry = await Db.HashGetAllAsync(BasketKey(buyerId));
            if (entry.Length == 0)
            {
                return ProjectedBasket.Unknown;
            }

            var stored = entry.FirstOrDefault(field => field.Name == "basket").Value;
            if (!stored.HasValue || stored.Length() == 0)
            {
                return ProjectedBasket.NoBasket;
            }

            var basket = JsonSerializer.Deserialize<BasketSnapshot>(stored!, JsonOptions);
            return basket is null ? ProjectedBasket.Unknown : ProjectedBasket.Found(basket);
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException or JsonException)
        {
            // Degrade to a query rather than to an error page: the projection is an accelerator, and
            // BasketService can still answer. Logged, because a gateway quietly querying for every
            // page view is a performance problem worth seeing.
            _logger.LogWarning(ex, "Could not read the basket projection for {BuyerId}; falling back to a query", buyerId);
            return ProjectedBasket.Unknown;
        }
    }

    public async Task<string?> OwnerOfAsync(int basketId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Db.StringGetAsync(OwnerKey(basketId));
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException)
        {
            _logger.LogWarning(ex, "Could not look up the owner of basket {BasketId}", basketId);
            return null;
        }
    }

    /// <summary>
    /// The one method that lets its failure out. The caller is a queue consumer, so a failure here
    /// should put the event back on the queue and try again — swallowing it would drop a basket
    /// change and leave the projection wrong until that basket changes again.
    /// </summary>
    public async Task ApplyAsync(BasketChangedIntegrationEvent changed, CancellationToken cancellationToken = default)
    {
        var basket = changed.Basket;
        var applied = (long)await Db.ScriptEvaluateAsync(ApplyScript,
            new[] { BasketKey(basket.BuyerId), InFlightKey(basket.BuyerId), OwnerKey(basket.BasketId) },
            new RedisValue[]
            {
                changed.OccurredAt.ToUnixTimeMilliseconds(),
                JsonSerializer.Serialize(basket, JsonOptions),
                (long)BasketProjectionLifetimes.Entry.TotalSeconds,
                basket.BuyerId
            });

        if (applied == 0)
        {
            _logger.LogDebug("Ignored a basket event for {BuyerId} older than what is projected", basket.BuyerId);
        }
    }

    public async Task RememberNoBasketAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        try
        {
            await Db.ScriptEvaluateAsync(RememberNoBasketScript,
                new[] { BasketKey(buyerId) },
                new RedisValue[] { (long)BasketProjectionLifetimes.NoBasket.TotalSeconds });
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException)
        {
            // Nothing is lost by failing to cache: the next read queries BasketService again.
            _logger.LogWarning(ex, "Could not cache the absence of a basket for {BuyerId}", buyerId);
        }
    }

    public async Task NoteWriteInFlightAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        try
        {
            await Db.ScriptEvaluateAsync(NoteInFlightScript,
                new[] { InFlightKey(buyerId) },
                new RedisValue[] { (long)BasketProjectionLifetimes.InFlightWrite.TotalSeconds });
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException)
        {
            // Deliberately swallowed. The command is already on the queue, so the caller must still
            // be told 202 — failing here would report a loss that did not happen. The cost is that
            // this buyer's next read may be served from a projection that is a moment behind.
            _logger.LogWarning(ex, "Could not mark a write in flight for {BuyerId}; their next read may be stale", buyerId);
        }
    }
}
