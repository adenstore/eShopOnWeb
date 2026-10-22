using Microsoft.eShopWeb.CatalogService.Domain.Interfaces;

namespace Microsoft.eShopWeb.CatalogService.Domain.Entities;

public class CatalogType : BaseEntity, IAggregateRoot
{
    public string Type { get; private set; }
    public CatalogType(string type)
    {
        Type = type;
    }
}
