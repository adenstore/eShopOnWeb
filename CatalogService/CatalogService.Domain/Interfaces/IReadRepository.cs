using Ardalis.Specification;

namespace Microsoft.eShopWeb.CatalogService.Domain.Interfaces;

public interface IReadRepository<T> : IReadRepositoryBase<T> where T : class, IAggregateRoot
{
}
