using Ardalis.Specification;
using Microsoft.eShopWeb.BasketService.Domain;

namespace Microsoft.eShopWeb.BasketService.Interfaces;

public interface IRepository<T> : IRepositoryBase<T> where T : class, IAggregateRoot
{
}
