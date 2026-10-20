using Ardalis.Result;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.BasketService.Dtos;
using Microsoft.eShopWeb.BasketService.Interfaces;

namespace Microsoft.eShopWeb.BasketService.Controllers;

[ApiController]
[Route("api/baskets")]
[Produces("application/json")]
public class BasketController : ControllerBase
{
    private readonly IBasketService _basketService;
    private readonly IBasketQueryService _basketQueryService;

    public BasketController(IBasketService basketService, IBasketQueryService basketQueryService)
    {
        _basketService = basketService;
        _basketQueryService = basketQueryService;
    }

    [HttpPost("{buyerId}")]
    [ProducesResponseType<BasketDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BasketDto>> GetOrCreate(string buyerId)
    {
        var basket = await _basketService.GetOrCreateBasketAsync(buyerId);

        return Ok(BasketDto.FromBasket(basket));
    }

    [HttpPost("{buyerId}/items")]
    [ProducesResponseType<BasketDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BasketDto>> AddItem(string buyerId, AddBasketItemRequest request)
    {
        var basket = await _basketService.AddItemToBasket(
            buyerId, request.CatalogItemId, request.UnitPrice, request.Quantity);

        return Ok(BasketDto.FromBasket(basket));
    }

    [HttpPut("{basketId:int}/items")]
    [ProducesResponseType<BasketDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BasketDto>> SetQuantities(int basketId, SetBasketQuantitiesRequest request)
    {
        var result = await _basketService.SetQuantities(basketId, request.Quantities);

        if (result.Status == ResultStatus.NotFound)
            return NotFound();

        return Ok(BasketDto.FromBasket(result.Value));
    }

    [HttpDelete("{basketId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int basketId)
    {
        try
        {
            await _basketService.DeleteBasketAsync(basketId);
        }
        catch (ArgumentNullException)
        {
            return NotFound();
        }

        return NoContent();
    }

    [HttpPost("transfer")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Transfer(TransferBasketRequest request)
    {
        await _basketService.TransferBasketAsync(request.AnonymousId, request.UserName);

        return NoContent();
    }

    [HttpGet("{buyerId}/items/count")]
    [ProducesResponseType<BasketItemCountResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BasketItemCountResponse>> ItemCount(string buyerId)
    {
        var count = await _basketQueryService.CountTotalBasketItems(buyerId);

        return Ok(new BasketItemCountResponse(count));
    }
}
