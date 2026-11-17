using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.BasketService.Contracts;

namespace Microsoft.eShopWeb.FunctionalTests.Web;

/// <summary>
/// Stands in for the basket so the Web app's functional tests don't need a gateway, a broker and
/// BasketService running. Replaces the monolith's in-process basket, which was deleted with the
/// rest of its basket code.
/// <para>
/// It mirrors what the gateway answers rather than what an in-process basket would do, because
/// that is what the pages are written against:
/// </para>
/// <list type="bullet">
/// <item>reading never creates — a buyer who has added nothing is reported at
/// <see cref="IBasketService.NoBasketId"/>, so <c>BasketViewModel.HasBasket</c> is exercised;</item>
/// <item>the writes return once the command is applied, which is the gateway's 202 plus the
/// read-your-own-writes guarantee it gives on the following read. The real path is asynchronous,
/// so these tests cannot catch a missing guarantee — see <c>src/ApiGateway/README.md</c> for where
/// that is covered;</item>
/// <item><see cref="SetQuantities"/> on a basket that does not exist is a no-op, not an error,
/// matching BasketService's <c>Result.NotFound</c> being swallowed by its message handler.</item>
/// </list>
/// <para>
/// Registered as a singleton: a basket has to outlive the request that created it, and each test
/// class gets its own <see cref="TestApplication"/>, so no state leaks between classes. Item ids
/// start at 1 per instance, which keeps them single-digit for
/// <see cref="WebPageHelpers.GetId(string)"/>'s regex.
/// </para>
/// </summary>
public class FakeBasketApiClient : IBasketService, IBasketQueryService
{
    private readonly object _lock = new();
    private readonly List<FakeBasket> _baskets = new();
    private int _nextBasketId;
    private int _nextItemId;

    public Task<BasketSnapshot> GetBasketAsync(string buyerId)
    {
        lock (_lock)
        {
            var basket = Find(buyerId);
            return Task.FromResult(basket is null
                ? BasketSnapshot.Empty(IBasketService.NoBasketId, buyerId)
                : basket.ToSnapshot());
        }
    }

    public Task<BasketSnapshot?> GetBasketAsync(int basketId)
    {
        lock (_lock)
        {
            // Null, not an empty basket: a missing basket must not present itself as one that is
            // merely empty, or checkout would place an order for nothing.
            return Task.FromResult(_baskets.SingleOrDefault(b => b.Id == basketId)?.ToSnapshot());
        }
    }

    public Task AddItemToBasket(string username, int catalogItemId, decimal price, int quantity = 1)
    {
        lock (_lock)
        {
            var basket = Find(username) ?? Create(username);
            basket.AddItem(catalogItemId, price, quantity, () => ++_nextItemId);
            return Task.CompletedTask;
        }
    }

    public Task SetQuantities(int basketId, Dictionary<string, int> quantities)
    {
        lock (_lock)
        {
            var basket = _baskets.SingleOrDefault(b => b.Id == basketId);
            if (basket is null) return Task.CompletedTask;

            foreach (var item in basket.Items)
            {
                if (quantities.TryGetValue(item.Id.ToString(), out var quantity))
                {
                    item.Quantity = quantity;
                }
            }
            basket.Items.RemoveAll(i => i.Quantity == 0);
            return Task.CompletedTask;
        }
    }

    public Task TransferBasketAsync(string anonymousId, string userName)
    {
        lock (_lock)
        {
            var anonymous = Find(anonymousId);
            if (anonymous is null) return Task.CompletedTask;

            var target = Find(userName) ?? Create(userName);
            // Copied, because a transfer onto the same buyer would otherwise add to the list being
            // iterated. Unreachable from the Login page (a cookie GUID is never a username), but a
            // fake that throws reads as a product bug.
            foreach (var item in anonymous.Items.ToList())
            {
                target.AddItem(item.CatalogItemId, item.UnitPrice, item.Quantity, () => ++_nextItemId);
            }
            _baskets.Remove(anonymous);
            return Task.CompletedTask;
        }
    }

    public Task<int> CountTotalBasketItems(string username)
    {
        lock (_lock)
        {
            return Task.FromResult(Find(username)?.Items.Sum(i => i.Quantity) ?? 0);
        }
    }

    private FakeBasket? Find(string buyerId) => _baskets.SingleOrDefault(b => b.BuyerId == buyerId);

    private FakeBasket Create(string buyerId)
    {
        // Ids start at 1 so that none of them is NoBasketId.
        var basket = new FakeBasket(++_nextBasketId, buyerId);
        _baskets.Add(basket);
        return basket;
    }

    private class FakeBasket
    {
        public FakeBasket(int id, string buyerId)
        {
            Id = id;
            BuyerId = buyerId;
        }

        public int Id { get; }

        public string BuyerId { get; }

        public List<FakeBasketItem> Items { get; } = new();

        public void AddItem(int catalogItemId, decimal unitPrice, int quantity, Func<int> nextItemId)
        {
            // Same line twice is one line with more on it, the way Basket.AddItem has always
            // behaved: the catalog item is what identifies a line, not the item id.
            var existing = Items.SingleOrDefault(i => i.CatalogItemId == catalogItemId);
            if (existing is not null)
            {
                existing.Quantity += quantity;
                return;
            }
            Items.Add(new FakeBasketItem(nextItemId(), catalogItemId, unitPrice) { Quantity = quantity });
        }

        public BasketSnapshot ToSnapshot() => new(Id, BuyerId,
            Items.Select(i => new BasketItemSnapshot(i.Id, i.CatalogItemId, i.UnitPrice, i.Quantity)).ToList());
    }

    private class FakeBasketItem
    {
        public FakeBasketItem(int id, int catalogItemId, decimal unitPrice)
        {
            Id = id;
            CatalogItemId = catalogItemId;
            UnitPrice = unitPrice;
        }

        public int Id { get; }

        public int CatalogItemId { get; }

        public decimal UnitPrice { get; }

        public int Quantity { get; set; }
    }
}
