using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.eShopWeb.ApiGateway.Endpoints;
using Microsoft.eShopWeb.BasketService.Contracts;
using Xunit;

namespace Microsoft.eShopWeb.ApiGateway.Tests;

/// <summary>
/// The gateway's write path: REST in, one basket command out. What matters is the translation —
/// which routing key, which command, which status code — plus the two things that are easy to get
/// wrong when a proxy and a publisher share one pipeline: Ocelot swallowing the basket routes, and
/// an unreachable broker being reported as success.
/// </summary>
public class BasketWriteEndpointsTests : IClassFixture<ApiGatewayApplication>
{
    private readonly HttpClient _client;
    private readonly RecordingBasketCommandPublisher _publisher;

    public BasketWriteEndpointsTests(ApiGatewayApplication factory)
    {
        _client = factory.CreateClient();
        _publisher = factory.Publisher;
        _publisher.Reset();
    }

    [Fact]
    public async Task AddingAnItemIsAcceptedAndPublishedAsACommand()
    {
        var response = await _client.PostAsJsonAsync("/api/baskets/buyer-1/items",
            new AddBasketItemRequest(CatalogItemId: 2, UnitPrice: 9.99m, Quantity: 3));

        // 202, not 200: the broker has the command, the basket has not changed yet.
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var (routingKey, command) = _publisher.Published.Should().ContainSingle().Subject;
        routingKey.Should().Be(BasketMessaging.RoutingKeys.AddItem);
        command.Should().BeEquivalentTo(new AddItemToBasketCommand
        {
            // From the URL, not the body: the buyer is the resource's identity.
            BuyerId = "buyer-1",
            CatalogItemId = 2,
            UnitPrice = 9.99m,
            Quantity = 3
        });
    }

    [Fact]
    public async Task SettingQuantitiesTakesTheBasketIdFromTheUrl()
    {
        var response = await _client.PutAsJsonAsync("/api/baskets/17/items",
            new SetBasketQuantitiesRequest(new Dictionary<string, int> { ["5"] = 2, ["6"] = 0 }));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var (routingKey, command) = _publisher.Published.Should().ContainSingle().Subject;
        routingKey.Should().Be(BasketMessaging.RoutingKeys.SetQuantities);
        command.Should().BeEquivalentTo(new SetBasketQuantitiesCommand
        {
            BasketId = 17,
            Quantities = new Dictionary<string, int> { ["5"] = 2, ["6"] = 0 }
        });
    }

    [Fact]
    public async Task TransferringABasketIsAcceptedAndPublishedAsACommand()
    {
        var response = await _client.PostAsJsonAsync("/api/baskets/transfer",
            new TransferBasketRequest("cookie-id", "me@example.com"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var (routingKey, command) = _publisher.Published.Should().ContainSingle().Subject;
        routingKey.Should().Be(BasketMessaging.RoutingKeys.Transfer);
        command.Should().BeEquivalentTo(new TransferBasketCommand
        {
            AnonymousId = "cookie-id",
            UserName = "me@example.com"
        });
    }

    [Theory]
    [InlineData(0, 1)] // no catalog item
    [InlineData(2, 0)] // no quantity
    public async Task AnInvalidCommandIsRejectedBeforeItIsPublished(int catalogItemId, int quantity)
    {
        var response = await _client.PostAsJsonAsync("/api/baskets/buyer-1/items",
            new AddBasketItemRequest(catalogItemId, UnitPrice: 9.99m, Quantity: quantity));

        // Caught here, where there is still someone to answer. On the queue the same command would
        // be dead-lettered to "invalid" and the shopper would just see nothing happen.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _publisher.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task ABrokerThatCannotBeReachedIsNotReportedAsAccepted()
    {
        _publisher.FailWith = new InvalidOperationException("broker is down");

        var response = await _client.PostAsJsonAsync("/api/baskets/buyer-1/items",
            new AddBasketItemRequest(CatalogItemId: 2, UnitPrice: 9.99m));

        // Retryable, and honest: nothing was queued, so 202 would lose the item silently.
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task OcelotStillAnswersThePathsTheGatewayDoesNotServeItself()
    {
        var response = await _client.GetAsync("/api/catalog-items");

        // Normally 502 from Ocelot, because nothing is listening downstream in a test — but a 200
        // if CatalogService happens to be running locally, which is why the assertion is on the one
        // answer that would mean something is broken. Ocelot is terminal middleware, so a 404 means
        // the request ran past it to the end of the pipeline and the proxy half of the gateway is
        // dead.
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
        _publisher.Published.Should().BeEmpty();
    }
}
