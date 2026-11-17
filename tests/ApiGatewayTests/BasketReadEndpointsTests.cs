using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.eShopWeb.ApiGateway.Endpoints;
using Microsoft.eShopWeb.ApiGateway.Projections;
using Microsoft.eShopWeb.BasketService.Contracts;
using Xunit;

namespace Microsoft.eShopWeb.ApiGateway.Tests;

/// <summary>
/// The gateway's read path, where the interesting decision is not what the response body says but
/// where it came from: the projection, or a query to BasketService. Every test here asserts both.
/// <para>
/// Each test uses its own buyer id. The host — and therefore the projection — is shared by the
/// class, and distinct ids are cheaper and clearer than resetting it between tests.
/// </para>
/// </summary>
public class BasketReadEndpointsTests : IClassFixture<ApiGatewayApplication>
{
    private readonly HttpClient _client;
    private readonly ApiGatewayApplication _gateway;
    private readonly FakeBasketQueryClient _queries;

    public BasketReadEndpointsTests(ApiGatewayApplication gateway)
    {
        _gateway = gateway;
        _client = gateway.CreateClient();
        _queries = gateway.Queries;
        _queries.Reset();
        gateway.Publisher.Reset();
    }

    [Fact]
    public async Task AProjectedBasketIsServedWithoutAskingTheService()
    {
        await ProjectAsync("projected-buyer", basketId: 11, (CatalogItemId: 4, Quantity: 2));

        var basket = await _client.GetFromJsonAsync<BasketSnapshot>("/api/baskets/projected-buyer");

        basket!.BasketId.Should().Be(11);
        basket.Items.Should().ContainSingle().Which.CatalogItemId.Should().Be(4);
        // The point of the projection: a basket render costs no broker round-trip.
        _queries.Asks.Should().Be(0);
    }

    [Fact]
    public async Task TheItemCountComesFromTheSameReadAsTheBasket()
    {
        await ProjectAsync("counted-buyer", basketId: 12, (CatalogItemId: 4, Quantity: 2), (CatalogItemId: 5, Quantity: 3));

        var count = await _client.GetFromJsonAsync<BasketItemCountResponse>("/api/baskets/counted-buyer/items/count");

        // Totalled from the same snapshot the page renders, so the header can never disagree with
        // the basket shown beside it.
        count!.Count.Should().Be(5);
        _queries.Asks.Should().Be(0);
    }

    [Fact]
    public async Task AReadRightAfterTheBuyersOwnWriteIsServedByTheService()
    {
        // The projection holds a basket with one item...
        await ProjectAsync("racing-buyer", basketId: 13, (CatalogItemId: 4, Quantity: 1));
        // ...and BasketService has the second one, which the gateway has not seen an event for yet.
        _queries.Answer("racing-buyer", BasketQueryReply.Found(Snapshot(13, "racing-buyer",
            (CatalogItemId: 4, Quantity: 1), (CatalogItemId: 5, Quantity: 1))));

        var write = await _client.PostAsJsonAsync("/api/baskets/racing-buyer/items",
            new AddBasketItemRequest(CatalogItemId: 5, UnitPrice: 1.50m, Quantity: 1));
        write.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var basket = await _client.GetFromJsonAsync<BasketSnapshot>("/api/baskets/racing-buyer");

        // Read-your-own-writes. Serving the projection here is the "I added it and it vanished" bug:
        // add-to-cart publishes one-way and redirects immediately, so this read happens before the
        // event does.
        basket!.Items.Should().HaveCount(2);
        _queries.Asks.Should().Be(1);
    }

    [Fact]
    public async Task TheEventForThatWritePutsReadsBackOnTheProjection()
    {
        await ProjectAsync("settled-buyer", basketId: 14, (CatalogItemId: 4, Quantity: 1));

        await _client.PostAsJsonAsync("/api/baskets/settled-buyer/items",
            new AddBasketItemRequest(CatalogItemId: 5, UnitPrice: 1.50m, Quantity: 1));
        await ProjectAsync("settled-buyer", basketId: 14, (CatalogItemId: 4, Quantity: 1), (CatalogItemId: 5, Quantity: 1));

        var basket = await _client.GetFromJsonAsync<BasketSnapshot>("/api/baskets/settled-buyer");

        // The in-flight guard is cleared by the event, not by a timeout: the projection has caught
        // up, so the round-trip stops immediately rather than when a clock says it may.
        basket!.Items.Should().HaveCount(2);
        _queries.Asks.Should().Be(0);
    }

    [Fact]
    public async Task ABuyerWithNoBasketGetsAnEmptyOneRatherThanANotFound()
    {
        var response = await _client.GetAsync("/api/baskets/empty-handed-buyer");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var basket = await response.Content.ReadFromJsonAsync<BasketSnapshot>();
        // Id 0 and no items: baskets are created on the first add, so for a first-time visitor
        // "no basket" is the normal case and 404 would make the common path an error path. A caller
        // keying off the id has to read 0 as "nothing to update yet".
        basket!.BasketId.Should().Be(0);
        basket.Items.Should().BeEmpty();
        basket.TotalItems.Should().Be(0);
    }

    [Fact]
    public async Task AnAbsentBasketIsRememberedSoBrowsingDoesNotQueryOnEveryPage()
    {
        await _client.GetAsync("/api/baskets/browsing-buyer");
        await _client.GetAsync("/api/baskets/browsing-buyer/items/count");
        await _client.GetAsync("/api/baskets/browsing-buyer");

        // One query for three reads. Without caching the absence, an anonymous shopper who has not
        // added anything — the commonest visitor in the shop — would cost a round-trip per page.
        _queries.Asks.Should().Be(1);
    }

    [Fact]
    public async Task CheckoutReadsTheBasketFromTheServiceEvenWhenItIsProjected()
    {
        await ProjectAsync("checkout-buyer", basketId: 15, (CatalogItemId: 4, Quantity: 1));
        _queries.Answer(15, BasketQueryReply.Found(Snapshot(15, "checkout-buyer",
            (CatalogItemId: 4, Quantity: 1), (CatalogItemId: 5, Quantity: 7))));

        var basket = await _client.GetFromJsonAsync<BasketSnapshot>("/api/baskets/15");

        // Never the projection, however fresh it looks: this is the read an order is built from,
        // where wrong items or wrong prices are a money bug rather than a cosmetic one.
        basket!.Items.Should().HaveCount(2);
        _queries.Asks.Should().Be(1);
    }

    [Fact]
    public async Task ABasketIdThatDoesNotExistIsANotFound()
    {
        var response = await _client.GetAsync("/api/baskets/999");

        // Unlike the by-buyer route: asking for a specific basket that is gone is a real miss, and
        // an empty basket would sail past the caller's null check and become an order with no items.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AServiceThatDoesNotAnswerInTimeIsAGatewayTimeout()
    {
        _queries.FailWith = new TimeoutException("no reply");

        var response = await _client.GetAsync("/api/baskets/unanswered-buyer");

        // 504, not 503: the question was asked and nothing came back, which is a service that is
        // behind rather than a broker that is unreachable.
        response.StatusCode.Should().Be(HttpStatusCode.GatewayTimeout);
    }

    [Fact]
    public async Task AQuestionThatCannotBeAskedAtAllIsServiceUnavailable()
    {
        _queries.FailWith = new InvalidOperationException("broker is down");

        var response = await _client.GetAsync("/api/baskets/unreachable-buyer");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task AServiceThatAnswersWithAnErrorIsABadGateway()
    {
        _queries.Answer("rejected-buyer", BasketQueryReply.Failed("malformed request"));

        var response = await _client.GetAsync("/api/baskets/rejected-buyer");

        // 502: the failure is upstream of the gateway and there is nothing the caller can fix.
        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task SettingQuantitiesGuardsTheOwningBuyersReads()
    {
        // The projection knows which buyer basket 16 belongs to, which is all the PUT route gets.
        await ProjectAsync("quantity-buyer", basketId: 16, (CatalogItemId: 4, Quantity: 1));
        _queries.Answer("quantity-buyer", BasketQueryReply.Found(Snapshot(16, "quantity-buyer",
            (CatalogItemId: 4, Quantity: 9))));

        await _client.PutAsJsonAsync("/api/baskets/16/items",
            new SetBasketQuantitiesRequest(new Dictionary<string, int> { ["1"] = 9 }));

        var basket = await _client.GetFromJsonAsync<BasketSnapshot>("/api/baskets/quantity-buyer");

        // Otherwise the redirect after "update quantities" renders the quantities from before it.
        basket!.Items.Should().ContainSingle().Which.Quantity.Should().Be(9);
        _queries.Asks.Should().Be(1);
    }

    private Task ProjectAsync(string buyerId, int basketId, params (int CatalogItemId, int Quantity)[] items) =>
        _gateway.Projection.ApplyAsync(
            BasketChangedIntegrationEvent.For(Snapshot(basketId, buyerId, items)));

    private static BasketSnapshot Snapshot(int basketId, string buyerId,
        params (int CatalogItemId, int Quantity)[] items) =>
        new(basketId, buyerId, items
            .Select((item, index) => new BasketItemSnapshot(index + 1, item.CatalogItemId, 1.50m, item.Quantity))
            .ToList());
}
