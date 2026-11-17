using System.Collections.Concurrent;
using Microsoft.eShopWeb.BasketService.Contracts;

namespace Microsoft.eShopWeb.ApiGateway.Projections;

/// <summary>
/// The same projection in process memory, for a gateway started without Redis
/// (<c>Projection:UseOnlyInMemoryStore</c>) — a local run or a test. LagerService draws the same
/// line with <c>UseOnlyInMemoryDatabase</c>.
/// <para>
/// It implements the identical rules, including the newer-wins apply and the clamped in-flight
/// count, because those rules are what the read endpoints are built on and a fake that cut corners
/// would make the tests over them worthless. It is not a substitute for Redis in a real run: the
/// projection is lost on restart, and two gateway instances would each keep their own.
/// </para>
/// </summary>
public sealed class InMemoryBasketProjection : IBasketProjection
{
    private sealed record Entry(long OccurredAtMs, BasketSnapshot? Basket, DateTimeOffset ExpiresAt);

    private sealed record InFlight(int Count, DateTimeOffset ExpiresAt);

    private readonly ConcurrentDictionary<string, Entry> _baskets = new();
    private readonly ConcurrentDictionary<int, string> _owners = new();
    private readonly ConcurrentDictionary<string, InFlight> _inFlight = new();
    private readonly TimeProvider _time;

    public InMemoryBasketProjection(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    public Task<ProjectedBasket> ReadAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        if (InFlightCount(buyerId) > 0)
        {
            return Task.FromResult(ProjectedBasket.Unknown);
        }

        var entry = Live(buyerId);
        if (entry is null)
        {
            return Task.FromResult(ProjectedBasket.Unknown);
        }

        return Task.FromResult(entry.Basket is null
            ? ProjectedBasket.NoBasket
            : ProjectedBasket.Found(entry.Basket));
    }

    public Task<string?> OwnerOfAsync(int basketId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_owners.TryGetValue(basketId, out var buyerId) ? buyerId : null);

    public Task ApplyAsync(BasketChangedIntegrationEvent changed, CancellationToken cancellationToken = default)
    {
        var basket = changed.Basket;
        var occurredAt = changed.OccurredAt.ToUnixTimeMilliseconds();

        if (Live(basket.BuyerId) is { } existing && existing.OccurredAtMs > occurredAt)
        {
            return Task.CompletedTask;
        }

        _baskets[basket.BuyerId] = new Entry(occurredAt, basket,
            _time.GetUtcNow() + BasketProjectionLifetimes.Entry);
        _owners[basket.BasketId] = basket.BuyerId;

        // Clamped at zero, and only for a count that is actually outstanding: BasketService also
        // publishes events this gateway did not command — a basket cleared by a placed order — and
        // those must not push the count below zero, or the next real write would be masked by it.
        var outstanding = InFlightCount(basket.BuyerId);
        if (outstanding > 0)
        {
            _inFlight[basket.BuyerId] = new InFlight(outstanding - 1,
                _time.GetUtcNow() + BasketProjectionLifetimes.InFlightWrite);
        }

        return Task.CompletedTask;
    }

    public Task RememberNoBasketAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        // Only if absent, for the reason in IBasketProjection: a query reply has no event timestamp
        // to order it against the events on the fanout, so it must never overwrite one.
        if (Live(buyerId) is null)
        {
            _baskets[buyerId] = new Entry(0, null, _time.GetUtcNow() + BasketProjectionLifetimes.NoBasket);
        }

        return Task.CompletedTask;
    }

    public Task NoteWriteInFlightAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        _inFlight[buyerId] = new InFlight(InFlightCount(buyerId) + 1,
            _time.GetUtcNow() + BasketProjectionLifetimes.InFlightWrite);

        return Task.CompletedTask;
    }

    /// <summary>The count of this buyer's own writes still outstanding, zero once the expiry lapses.</summary>
    private int InFlightCount(string buyerId) =>
        _inFlight.TryGetValue(buyerId, out var inFlight) && inFlight.ExpiresAt > _time.GetUtcNow()
            ? inFlight.Count
            : 0;

    /// <summary>The buyer's entry, or null if there is none or it has expired.</summary>
    private Entry? Live(string buyerId) =>
        _baskets.TryGetValue(buyerId, out var entry) && entry.ExpiresAt > _time.GetUtcNow()
            ? entry
            : null;
}
