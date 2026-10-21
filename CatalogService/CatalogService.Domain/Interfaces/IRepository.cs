using Ardalis.Specification;

namespace Microsoft.eShopWeb.CatalogService.Domain.Interfaces;

public interface IRepository<T> : IRepositoryBase<T> where T : class, IAggregateRoot
{
}
