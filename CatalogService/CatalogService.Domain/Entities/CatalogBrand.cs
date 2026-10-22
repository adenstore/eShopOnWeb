using Microsoft.eShopWeb.CatalogService.Domain.Interfaces;

namespace Microsoft.eShopWeb.CatalogService.Domain.Entities;

public class CatalogBrand : BaseEntity, IAggregateRoot
{
    public string Brand { get; private set; }
    public CatalogBrand(string brand)
    {
        Brand = brand;
    }
}
