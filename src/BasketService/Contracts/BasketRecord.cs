using Microsoft.eShopWeb.BasketService.Domain;

namespace Contracts;

public record BasketRecord(string buyerId, List<BasketItem> items);
