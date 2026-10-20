using Ardalis.Specification.EntityFrameworkCore;
using Microsoft.eShopWeb.BasketService.Domain;
using Microsoft.eShopWeb.BasketService.Interfaces;

namespace Microsoft.eShopWeb.BasketService.Data;

public class EfRepository<T> : RepositoryBase<T>, IRepository<T> where T : class, IAggregateRoot
{
    public EfRepository(BasketDbContext dbContext) : base(dbContext)
    {
    }
}
