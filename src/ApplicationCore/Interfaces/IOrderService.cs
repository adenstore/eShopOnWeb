using System.Threading.Tasks;
using Microsoft.eShopWeb.OrderService.Contracts;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderService
{
    Task<int> CreateOrderAsync(int basketId, AddressDto shippingAddress);
}
