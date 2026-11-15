using FluentAssertions;
using Microsoft.eShopWeb.ApiGateway.Projections;
using Microsoft.eShopWeb.BasketService.Contracts;
using Xunit;

namespace Microsoft.eShopWeb.ApiGateway.Tests;

/// <summary>
/// The projection's rules, tested directly rather than through HTTP: these are the claims the read
/// endpoints are built on, and each of them exists because of a specific way a projection goes
/// wrong. The Redis implementation enforces the same rules in Lua, server-side, so that the
/// compare and the write are one step — see <see cref="RedisBasketProjection"/>.
/// </summary>
public class BasketProjectionTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ANewerEventReplacesWhatIsProjected()
    {
        var projection = new InMemoryBasketProjection();

        await projection.ApplyAsync(Changed(Noon, Snapshot(1, "buyer", items: 1)));
        await projection.ApplyAsync(Changed(Noon.AddSeconds(1), Snapshot(1, "buyer", items: 3)));

        var projected = await projection.ReadAsync("buyer");
        projected.Basket!.Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task AnOlderEventIsIgnored()
    {
        var projection = new InMemoryBasketProjection();

        await projection.ApplyAsync(Changed(Noon.AddSeconds(1), Snapshot(1, "buyer", items: 3)));
        await projection.ApplyAsync(Changed(Noon, Snapshot(1, "buyer", items: 1)));

        // A redelivery after a broker or consumer restart can arrive behind a newer event. Applying
        // it would resurrect items the shopper has already removed.
        var projected = await projection.ReadAsync("buyer");
        projected.Basket!.Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task AWriteInFlightMakesTheProjectionDeclineToAnswer()
    {
        var projection = new InMemoryBasketProjection();
        await projection.ApplyAsync(Changed(Noon, Snapshot(1, "buyer", items: 1)));

        await projection.NoteWriteInFlightAsync("buyer");

        var projected = await projection.ReadAsync("buyer");
        projected.Kind.Should().Be(ProjectedBasketKind.Unknown);
    }

    [Fact]
    public async Task TheEventForThatWriteLetsItAnswerAgain()
    {
        var projection = new InMemoryBasketProjection();
        await projection.NoteWriteInFlightAsync("buyer");

        await projection.ApplyAsync(Changed(Noon, Snapshot(1, "buyer", items: 1)));

        var projected = await projection.ReadAsync("buyer");
        projected.Kind.Should().Be(ProjectedBasketKind.Basket);
    }

    [Fact]
    public async Task TwoWritesNeedTwoEvents()
    {
        var projection = new InMemoryBasketProjection();
        await projection.NoteWriteInFlightAsync("buyer");
        await projection.NoteWriteInFlightAsync("buyer");

        await projection.ApplyAsync(Changed(Noon, Snapshot(1, "buyer", items: 1)));

        // Counted, not flagged: a shopper clicking "add" twice has two commands outstanding, and the
        // first event only proves the projection has caught up with the first of them.
        (await projection.ReadAsync("buyer")).Kind.Should().Be(ProjectedBasketKind.Unknown);

        await projection.ApplyAsync(Changed(Noon.AddSeconds(1), Snapshot(1, "buyer", items: 2)));

        (await projection.ReadAsync("buyer")).Kind.Should().Be(ProjectedBasketKind.Basket);
    }

    [Fact]
    public async Task AnEventTheGatewayDidNotCommandDoesNotMaskTheNextWrite()
    {
        var projection = new InMemoryBasketProjection();

        // BasketService clears a basket by itself when an order is placed, so events arrive that no
        // command of this gateway's caused. Left unclamped, this one would drive the count to -1 and
        // the write below would bring it back to 0 — leaving the read trusting a projection that
        // does not have that write yet.
        await projection.ApplyAsync(Changed(Noon, BasketSnapshot.Empty(1, "buyer")));
        await projection.NoteWriteInFlightAsync("buyer");

        (await projection.ReadAsync("buyer")).Kind.Should().Be(ProjectedBasketKind.Unknown);
    }

    [Fact]
    public async Task AnInFlightWriteThatNeverProducesAnEventStopsBlockingReads()
    {
        var time = new TestTime(Noon);
        var projection = new InMemoryBasketProjection(time);
        await projection.ApplyAsync(Changed(Noon, Snapshot(1, "buyer", items: 1)));

        // Some commands produce no event at all: a SetQuantities for a basket that is already
        // checked out, a transfer with nothing to transfer, or an event BasketService failed to
        // publish. Without an expiry those reads would be served from BasketService forever.
        await projection.NoteWriteInFlightAsync("buyer");
        time.Advance(BasketProjectionLifetimes.InFlightWrite + TimeSpan.FromSeconds(1));

        (await projection.ReadAsync("buyer")).Kind.Should().Be(ProjectedBasketKind.Basket);
    }

    [Fact]
    public async Task ACachedAbsenceNeverOverwritesAProjectedBasket()
    {
        var projection = new InMemoryBasketProjection();
        await projection.ApplyAsync(Changed(Noon, Snapshot(1, "buyer", items: 1)));

        // A query reply carries no event timestamp, so there is nothing to order it against the
        // events on the fanout — which means it must never be allowed to overwrite one, however
        // recently it was asked.
        await projection.RememberNoBasketAsync("buyer");

        (await projection.ReadAsync("buyer")).Kind.Should().Be(ProjectedBasketKind.Basket);
    }

    [Fact]
    public async Task AnyEventSupersedesACachedAbsence()
    {
        var projection = new InMemoryBasketProjection();
        await projection.RememberNoBasketAsync("buyer");

        await projection.ApplyAsync(Changed(Noon, Snapshot(1, "buyer", items: 1)));

        (await projection.ReadAsync("buyer")).Kind.Should().Be(ProjectedBasketKind.Basket);
    }

    [Fact]
    public async Task ACachedAbsenceIsADifferentAnswerFromNotKnowing()
    {
        var projection = new InMemoryBasketProjection();

        (await projection.ReadAsync("unseen-buyer")).Kind.Should().Be(ProjectedBasketKind.Unknown);

        await projection.RememberNoBasketAsync("unseen-buyer");

        // The distinction is the whole value of caching an absence: "I have never heard of them"
        // has to ask BasketService, "they have no basket" does not.
        (await projection.ReadAsync("unseen-buyer")).Kind.Should().Be(ProjectedBasketKind.NoBasket);
    }

    [Fact]
    public async Task ProjectingABasketRecordsWhoOwnsIt()
    {
        var projection = new InMemoryBasketProjection();

        await projection.ApplyAsync(Changed(Noon, Snapshot(42, "buyer", items: 1)));

        // Only the basket id reaches PUT /api/baskets/{basketId}/items, and the in-flight guard is
        // keyed by buyer, so the write path needs this to know whose reads to stop trusting.
        (await projection.OwnerOfAsync(42)).Should().Be("buyer");
    }

    private static BasketChangedIntegrationEvent Changed(DateTimeOffset occurredAt, BasketSnapshot basket) =>
        new(Guid.NewGuid(), occurredAt, basket);

    private static BasketSnapshot Snapshot(int basketId, string buyerId, int items) =>
        new(basketId, buyerId, Enumerable.Range(1, items)
            .Select(i => new BasketItemSnapshot(i, i, 1.50m, 1))
            .ToList());

    /// <summary>A clock the test moves by hand, so an expiry can be reached without waiting for it.</summary>
    private sealed class TestTime : TimeProvider
    {
        private DateTimeOffset _now;

        public TestTime(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
