using System.Collections.Concurrent;
using Microsoft.eShopWeb.ApiGateway.Messaging;
using Microsoft.eShopWeb.BasketService.Contracts;

namespace Microsoft.eShopWeb.ApiGateway.Tests;

/// <summary>
/// Stands in for BasketService on the read side. It counts the questions as well as answering them,
/// because "how many times did the gateway have to ask" is the whole claim the projection makes —
/// a test that only checked the response body would pass just as happily with no projection at all.
/// </summary>
public sealed class FakeBasketQueryClient : IBasketQueryClient
{
    private readonly ConcurrentDictionary<string, BasketQueryReply> _byBuyer = new();
    private readonly ConcurrentDictionary<int, BasketQueryReply> _byId = new();
    private int _asks;

    /// <summary>Set to make the next query throw, as an unreachable broker or a silent service does.</summary>
    public Exception? FailWith { get; set; }

    /// <summary>How many queries have reached the service.</summary>
    public int Asks => _asks;

    public void Reset()
    {
        _byBuyer.Clear();
        _byId.Clear();
        FailWith = null;
        Interlocked.Exchange(ref _asks, 0);
    }

    public void Answer(string buyerId, BasketQueryReply reply) => _byBuyer[buyerId] = reply;

    public void Answer(int basketId, BasketQueryReply reply) => _byId[basketId] = reply;

    public Task<BasketQueryReply> GetByBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _asks);
        if (FailWith is not null)
        {
            return Task.FromException<BasketQueryReply>(FailWith);
        }

        // Unanswered means the buyer has no basket, which is what BasketService says for anyone who
        // has not added anything yet.
        return Task.FromResult(_byBuyer.TryGetValue(buyerId, out var reply)
            ? reply
            : BasketQueryReply.NotFound());
    }

    public Task<BasketQueryReply> GetByIdAsync(int basketId, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _asks);
        if (FailWith is not null)
        {
            return Task.FromException<BasketQueryReply>(FailWith);
        }

        return Task.FromResult(_byId.TryGetValue(basketId, out var reply)
            ? reply
            : BasketQueryReply.NotFound());
    }
}
