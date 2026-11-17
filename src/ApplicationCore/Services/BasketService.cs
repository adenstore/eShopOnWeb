using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.BasketAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.BasketService.Contracts;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// The in-process basket, kept against the monolith's own database.
/// <para>
/// No longer registered: the Web app reaches the basket through the ApiGateway
/// (<c>BasketApiClient</c>), which publishes to BasketService over RabbitMQ. This is retained as a
/// working drop-in for <see cref="IBasketService"/> so the cut-over can be reversed by changing one
/// DI line, and it is deleted — with the entities, the EF configuration and the tables — once the
/// gateway path has been verified end to end.
/// </para>
/// </summary>
public class BasketService : IBasketService
{
    private readonly IRepository<Basket> _basketRepository;
    private readonly IAppLogger<BasketService> _logger;

    public BasketService(IRepository<Basket> basketRepository,
        IAppLogger<BasketService> logger)
    {
        _basketRepository = basketRepository;
        _logger = logger;
    }

    public async Task<BasketSnapshot> GetBasketAsync(string buyerId)
    {
        var basketSpec = new BasketWithItemsSpecification(buyerId);
        var basket = await _basketRepository.FirstOrDefaultAsync(basketSpec);

        // Reads do not create, matching BasketService: a basket comes into existence on the first
        // add. Creating one here would also mean a write on every page view by a shopper who is
        // only browsing.
        return basket is null
            ? BasketSnapshot.Empty(IBasketService.NoBasketId, buyerId)
            : ToSnapshot(basket);
    }

    public async Task<BasketSnapshot?> GetBasketAsync(int basketId)
    {
        var basketSpec = new BasketWithItemsSpecification(basketId);
        var basket = await _basketRepository.FirstOrDefaultAsync(basketSpec);

        return basket is null ? null : ToSnapshot(basket);
    }

    public async Task AddItemToBasket(string username, int catalogItemId, decimal price, int quantity = 1)
    {
        var basketSpec = new BasketWithItemsSpecification(username);
        var basket = await _basketRepository.FirstOrDefaultAsync(basketSpec);

        if (basket == null)
        {
            basket = new Basket(username);
            await _basketRepository.AddAsync(basket);
        }

        basket.AddItem(catalogItemId, price, quantity);

        await _basketRepository.UpdateAsync(basket);
    }

    /// <summary>
    /// Not part of <see cref="IBasketService"/> any more: the basket is cleared by BasketService
    /// when it sees OrderService's OrderCreated event, so no caller asks for this. Kept so this
    /// class stays a complete in-process basket until it is deleted.
    /// </summary>
    public async Task DeleteBasketAsync(int basketId)
    {
        var basket = await _basketRepository.GetByIdAsync(basketId);
        Guard.Against.Null(basket, nameof(basket));
        await _basketRepository.DeleteAsync(basket);
    }

    public async Task SetQuantities(int basketId, Dictionary<string, int> quantities)
    {
        var basketSpec = new BasketWithItemsSpecification(basketId);
        var basket = await _basketRepository.FirstOrDefaultAsync(basketSpec);

        // A missing basket is a no-op, not a Result.NotFound: a one-way command has no caller left
        // to answer, so BasketService drops this case too rather than dead-lettering it.
        if (basket == null) return;

        foreach (var item in basket.Items)
        {
            if (quantities.TryGetValue(item.Id.ToString(), out var quantity))
            {
                if (_logger != null) _logger.LogInformation($"Updating quantity of item ID:{item.Id} to {quantity}.");
                item.SetQuantity(quantity);
            }
        }
        basket.RemoveEmptyItems();
        await _basketRepository.UpdateAsync(basket);
    }

    public async Task TransferBasketAsync(string anonymousId, string userName)
    {
        var anonymousBasketSpec = new BasketWithItemsSpecification(anonymousId);
        var anonymousBasket = await _basketRepository.FirstOrDefaultAsync(anonymousBasketSpec);
        if (anonymousBasket == null) return;
        var userBasketSpec = new BasketWithItemsSpecification(userName);
        var userBasket = await _basketRepository.FirstOrDefaultAsync(userBasketSpec);
        if (userBasket == null)
        {
            userBasket = new Basket(userName);
            await _basketRepository.AddAsync(userBasket);
        }
        foreach (var item in anonymousBasket.Items)
        {
            userBasket.AddItem(item.CatalogItemId, item.UnitPrice, item.Quantity);
        }
        await _basketRepository.UpdateAsync(userBasket);
        await _basketRepository.DeleteAsync(anonymousBasket);
    }

    // The published contract is the return type now, so there is one wire shape for a basket
    // instead of a DTO per implementation. Mirrors BasketSnapshotMapper in BasketService; it lives
    // here rather than on BasketSnapshot because the contracts assembly must not reference the
    // monolith's entities.
    private static BasketSnapshot ToSnapshot(Basket basket) => new(
        basket.Id,
        basket.BuyerId,
        basket.Items.Select(item => new BasketItemSnapshot(
            item.Id, item.CatalogItemId, item.UnitPrice, item.Quantity)).ToList());
}
