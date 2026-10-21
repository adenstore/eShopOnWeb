using Ardalis.Specification.EntityFrameworkCore;
using Microsoft.eShopWeb.CatalogService.Domain.Interfaces;

namespace Microsoft.eShopWeb.CatalogService.Infrastructure.Data;

public class EfRepository<T> : RepositoryBase<T>, IReadRepository<T>, IRepository<T> where T : class, IAggregateRoot
{
    public EfRepository(CatalogContext dbContext) : base(dbContext)
    {
    }
}
