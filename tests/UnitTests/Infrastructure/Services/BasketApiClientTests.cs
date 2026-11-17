using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Services;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Services;

/// <summary>
/// Pins the wire contract between the Web app and the ApiGateway's basket routes: the paths, the
/// request bodies, and that the gateway's own <c>BasketSnapshot</c> payload deserializes into the
/// type the application uses. Nothing else in the suite covers that seam — the functional tests
/// substitute the basket in process, and the gateway's own tests stop at its edge.
/// </summary>
public class BasketApiClientTests
{
    // A cookie id, the anonymous case. No characters that URL-escaping would change, so the path
    // assertions below say what they look like they say.
    private const string BuyerId = "a0a0a0a0-0000-0000-0000-000000000001";

    [Fact]
    public async Task ReadsTheBasketIdFromTheContractsOwnFieldName()
    {
        // The gateway answers with the published contract, where the id is "basketId". Reading it
        // into anything that calls the field "id" yields 0 instead — and 0 is the one value with a
        // meaning of its own, so it would present as "every shopper has an empty basket" rather
        // than as an error.
        var (client, handler) = ClientFor(HttpStatusCode.OK,
            """
            {"basketId":77,"buyerId":"buyer@example.com",
             "items":[{"id":17,"catalogItemId":7,"unitPrice":12.5,"quantity":3}],"totalItems":3}
            """);

        var basket = await client.GetBasketAsync("buyer@example.com");

        Assert.Equal(HttpMethod.Get, handler.Request!.Method);
        Assert.Equal(77, basket.BasketId);
        Assert.Equal("buyer@example.com", basket.BuyerId);
        Assert.Equal(3, basket.TotalItems);
        var item = Assert.Single(basket.Items);
        Assert.Equal(17, item.Id);
        Assert.Equal(7, item.CatalogItemId);
        Assert.Equal(12.5m, item.UnitPrice);
        Assert.Equal(3, item.Quantity);
    }

    [Fact]
    public async Task ReadsABuyersBasketWithoutCreatingOne()
    {
        var (client, handler) = ClientFor(HttpStatusCode.OK,
            $$"""{"basketId":0,"buyerId":"{{BuyerId}}","items":[],"totalItems":0}""");

        var basket = await client.GetBasketAsync(BuyerId);

        // GET, not POST: a buyer with no basket gets an empty one at NoBasketId rather than having
        // one created for them by looking at a page.
        Assert.Equal(HttpMethod.Get, handler.Request!.Method);
        Assert.Equal($"/api/baskets/{BuyerId}", handler.Request.RequestUri!.AbsolutePath);
        Assert.Equal(IBasketService.NoBasketId, basket.BasketId);
        Assert.Empty(basket.Items);
    }

    [Fact]
    public async Task ReturnsNullForABasketThatIsNotThere()
    {
        var (client, handler) = ClientFor(HttpStatusCode.NotFound);

        var basket = await client.GetBasketAsync(77);

        // Null, not an empty basket: checkout guards on null, and an empty basket would sail past
        // that guard and build an order with no items.
        Assert.Null(basket);
        Assert.Equal("/api/baskets/77", handler.Request!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task PostsAnItemAndAcceptsTheGatewaysAccepted()
    {
        var (client, handler) = ClientFor(HttpStatusCode.Accepted);

        await client.AddItemToBasket(BuyerId, 7, 12.5m, 2);

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal($"/api/baskets/{BuyerId}/items", handler.Request.RequestUri!.AbsolutePath);
        // Priced by the caller: BasketService holds no catalog data, so it cannot look the price up.
        Assert.Equal("""{"catalogItemId":7,"unitPrice":12.5,"quantity":2}""", handler.Body);
    }

    [Fact]
    public async Task PutsQuantitiesKeyedByBasketItemId()
    {
        var (client, handler) = ClientFor(HttpStatusCode.Accepted);

        await client.SetQuantities(77, new Dictionary<string, int> { ["17"] = 3, ["18"] = 0 });

        Assert.Equal(HttpMethod.Put, handler.Request!.Method);
        Assert.Equal("/api/baskets/77/items", handler.Request.RequestUri!.AbsolutePath);
        // Keys stay as written — a naming policy applied to dictionary keys would renumber nothing
        // but would break the match against BasketItemSnapshot.Id on the service side.
        Assert.Equal("""{"quantities":{"17":3,"18":0}}""", handler.Body);
    }

    [Fact]
    public async Task ThrowsWhenTheGatewayRejectsACommandForBasketZero()
    {
        var (client, _) = ClientFor(HttpStatusCode.BadRequest);

        // This is why the basket pages check HasBasket before posting: the gateway's
        // [Range(1, int.MaxValue)] on SetBasketQuantitiesCommand.BasketId answers 400, and
        // EnsureSuccessStatusCode turns that into a 500 page.
        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.SetQuantities(IBasketService.NoBasketId, new Dictionary<string, int>()));
    }

    [Fact]
    public async Task PostsATransferAtSignIn()
    {
        var (client, handler) = ClientFor(HttpStatusCode.Accepted);

        await client.TransferBasketAsync(BuyerId, "buyer@example.com");

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("/api/baskets/transfer", handler.Request.RequestUri!.AbsolutePath);
        Assert.Equal($$"""{"anonymousId":"{{BuyerId}}","userName":"buyer@example.com"}""", handler.Body);
    }

    [Fact]
    public async Task CountsItemsOnItsOwnRoute()
    {
        var (client, handler) = ClientFor(HttpStatusCode.OK, """{"count":5}""");

        var count = await client.CountTotalBasketItems(BuyerId);

        // Its own route rather than counting a whole basket here: this renders on every page of
        // the shop, and the gateway serves it from a projection.
        Assert.Equal(5, count);
        Assert.Equal($"/api/baskets/{BuyerId}/items/count", handler.Request!.RequestUri!.AbsolutePath);
    }

    private static (BasketApiClient Client, StubHandler Handler) ClientFor(
        HttpStatusCode status, string? json = null)
    {
        var handler = new StubHandler(status, json);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://gateway/") };

        return (new BasketApiClient(httpClient), handler);
    }

    /// <summary>Answers one canned response and records the request that asked for it.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public StubHandler(HttpStatusCode status, string? json)
        {
            _response = new HttpResponseMessage(status);
            if (json is not null)
            {
                _response.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }
        }

        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            if (request.Content is not null)
            {
                Body = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return _response;
        }
    }
}
