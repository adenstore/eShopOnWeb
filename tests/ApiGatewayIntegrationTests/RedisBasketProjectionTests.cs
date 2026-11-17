using FluentAssertions;
using Microsoft.eShopWeb.ApiGateway.Projections;
using Microsoft.eShopWeb.BasketService.Contracts;
using Xunit;

namespace Microsoft.eShopWeb.ApiGateway.IntegrationTests;

/// <summary>
/// The projection's rules against a real Redis. They are the same rules
/// <c>ApiGatewayTests.BasketProjectionTests</c> asserts on the in-memory implementation, and that is
/// the point: the Redis implementation expresses them in Lua, and these tests are what says the
/// transcription is faithful. Everything below is a rule the read endpoints depend on.
/// <para>
/// Each test uses its own buyer id and basket id, because the container is shared by the class.
/// </para>
/// </summary>
public class RedisBasketProjectionTests : IClassFixture<RedisFixture>
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly RedisFixture _redis;
    private readonly IBasketProjection _projection;

    public RedisBasketProjectionTests(RedisFixture redis)
    {
        _redis = redis;
        _projection = redis.Projection();
    }

    [Fact]
    public async Task ANewerEventReplacesWhatIsProjected()
    {
        await _projection.ApplyAsync(Changed(Noon, Snapshot(1, "newer", items: 1)));
        await _projection.ApplyAsync(Changed(Noon.AddSeconds(1), Snapshot(1, "newer", items: 3)));

        var projected = await _projection.ReadAsync("newer");

        projected.Kind.Should().Be(ProjectedBasketKind.Basket);
        projected.Basket!.Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task AnOlderEventIsIgnored()
    {
        await _projection.ApplyAsync(Changed(Noon.AddSeconds(1), Snapshot(2, "older", items: 3)));
        await _projection.ApplyAsync(Changed(Noon, Snapshot(2, "older", items: 1)));

        // A redelivery after a broker or consumer restart can arrive behind a newer event. Applying
        // it would resurrect items the shopper has already removed.
        var projected = await _projection.ReadAsync("older");
        projected.Basket!.Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task EventsAMillisecondApartAreStillOrdered()
    {
        await _projection.ApplyAsync(Changed(Noon.AddMilliseconds(1), Snapshot(3, "precise", items: 3)));
        await _projection.ApplyAsync(Changed(Noon, Snapshot(3, "precise", items: 1)));

        // The comparison happens in Lua, whose numbers are doubles — exact only to 2^53. Unix
        // milliseconds leave a wide margin; .NET ticks would not, and would quietly round events a
        // few microseconds apart into a tie.
        var projected = await _projection.ReadAsync("precise");
        projected.Basket!.Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task AWriteInFlightMakesTheProjectionDeclineToAnswer()
    {
        await _projection.ApplyAsync(Changed(Noon, Snapshot(4, "writing", items: 1)));

        await _projection.NoteWriteInFlightAsync("writing");

        // Read-your-own-writes: add-to-cart publishes one-way and redirects immediately, so this
        // read can happen before the event does.
        var projected = await _projection.ReadAsync("writing");
        projected.Kind.Should().Be(ProjectedBasketKind.Unknown);
    }

    [Fact]
    public async Task TheEventForThatWriteLetsItAnswerAgain()
    {
        await _projection.NoteWriteInFlightAsync("settled");

        await _projection.ApplyAsync(Changed(Noon, Snapshot(5, "settled", items: 1)));

        // Cleared by the event, not by a timeout: the round-trip stops as soon as the projection
        // has caught up rather than when a clock says it may have.
        var projected = await _projection.ReadAsync("settled");
        projected.Kind.Should().Be(ProjectedBasketKind.Basket);
    }

    [Fact]
    public async Task TwoWritesNeedTwoEvents()
    {
        await _projection.NoteWriteInFlightAsync("double");
        await _projection.NoteWriteInFlightAsync("double");

        await _projection.ApplyAsync(Changed(Noon, Snapshot(6, "double", items: 1)));

        // Counted, not flagged: a shopper clicking "add" twice has two commands outstanding, and the
        // first event only proves the projection has caught up with the first of them.
        (await _projection.ReadAsync("double")).Kind.Should().Be(ProjectedBasketKind.Unknown);

        await _projection.ApplyAsync(Changed(Noon.AddSeconds(1), Snapshot(6, "double", items: 2)));

        (await _projection.ReadAsync("double")).Kind.Should().Be(ProjectedBasketKind.Basket);
    }

    [Fact]
    public async Task AnEventTheGatewayDidNotCommandDoesNotMaskTheNextWrite()
    {
        // BasketService clears a basket by itself when an order is placed, so events arrive that no
        // command of this gateway's caused. This is the case the Lua clamp exists for: an
        // unclamped DECR would take the count to -1, and the write below would bring it back to 0,
        // leaving the read trusting a projection that does not have that write yet.
        await _projection.ApplyAsync(Changed(Noon, BasketSnapshot.Empty(7, "uncommanded")));
        await _projection.NoteWriteInFlightAsync("uncommanded");

        (await _projection.ReadAsync("uncommanded")).Kind.Should().Be(ProjectedBasketKind.Unknown);
    }

    [Fact]
    public async Task AnInFlightWriteIsGivenAnExpiry()
    {
        await _projection.NoteWriteInFlightAsync("expiring");

        // The backstop for commands that produce no event at all — a SetQuantities for a basket
        // already checked out, a transfer with nothing to transfer, an event BasketService failed to
        // publish. Without it those reads would be served from BasketService forever. Asserted on
        // the key rather than by waiting it out, since the expiring itself is Redis's job.
        var ttl = await _redis.Database.KeyTimeToLiveAsync("basket:pending:expiring");

        ttl.Should().NotBeNull();
        ttl!.Value.Should().BeLessThanOrEqualTo(BasketProjectionLifetimes.InFlightWrite);
        ttl.Value.Should().BeGreaterThan(BasketProjectionLifetimes.InFlightWrite - TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ACachedAbsenceNeverOverwritesAProjectedBasket()
    {
        await _projection.ApplyAsync(Changed(Noon, Snapshot(8, "has-basket", items: 1)));

        // A query reply carries no event timestamp, so there is nothing to order it against the
        // events on the fanout — which means it must never be allowed to overwrite one, however
        // recently it was asked.
        await _projection.RememberNoBasketAsync("has-basket");

        (await _projection.ReadAsync("has-basket")).Kind.Should().Be(ProjectedBasketKind.Basket);
    }

    [Fact]
    public async Task AnyEventSupersedesACachedAbsence()
    {
        await _projection.RememberNoBasketAsync("then-buys");

        await _projection.ApplyAsync(Changed(Noon, Snapshot(9, "then-buys", items: 1)));

        (await _projection.ReadAsync("then-buys")).Kind.Should().Be(ProjectedBasketKind.Basket);
    }

    [Fact]
    public async Task ACachedAbsenceIsADifferentAnswerFromNotKnowing()
    {
        (await _projection.ReadAsync("unseen")).Kind.Should().Be(ProjectedBasketKind.Unknown);

        await _projection.RememberNoBasketAsync("unseen");

        // The distinction is the whole value of caching an absence: "I have never heard of them"
        // has to ask BasketService, "they have no basket" does not — and an anonymous shopper who
        // has added nothing yet is the commonest visitor in the shop.
        (await _projection.ReadAsync("unseen")).Kind.Should().Be(ProjectedBasketKind.NoBasket);
    }

    [Fact]
    public async Task ProjectingABasketRecordsWhoOwnsIt()
    {
        await _projection.ApplyAsync(Changed(Noon, Snapshot(42, "owner", items: 1)));

        // Only the basket id reaches PUT /api/baskets/{basketId}/items, and the in-flight guard is
        // keyed by buyer, so the write path needs this to know whose reads to stop trusting.
        (await _projection.OwnerOfAsync(42)).Should().Be("owner");
    }

    [Fact]
    public async Task TheProjectionIsInRedisRatherThanInTheGateway()
    {
        await _projection.ApplyAsync(Changed(Noon, Snapshot(10, "shared", items: 2)));

        // A second instance, as a restarted gateway or a second replica would be. If this read came
        // back empty the projection would be process state wearing Redis's name, and every restart
        // would empty every shopper's basket until they changed it again.
        var elsewhere = await _redis.Projection().ReadAsync("shared");

        elsewhere.Basket!.Items.Should().HaveCount(2);
    }

    private static BasketChangedIntegrationEvent Changed(DateTimeOffset occurredAt, BasketSnapshot basket) =>
        new(Guid.NewGuid(), occurredAt, basket);

    private static BasketSnapshot Snapshot(int basketId, string buyerId, int items) =>
        new(basketId, buyerId, Enumerable.Range(1, items)
            .Select(i => new BasketItemSnapshot(i, i, 1.50m, 1))
            .ToList());
}
