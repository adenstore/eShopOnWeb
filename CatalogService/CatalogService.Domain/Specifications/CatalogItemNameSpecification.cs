using Ardalis.Specification;
using Microsoft.eShopWeb.CatalogService.Domain.Entities;

namespace Microsoft.eShopWeb.CatalogService.Domain.Specifications;

public class CatalogItemNameSpecification : Specification<CatalogItem>
{
    public CatalogItemNameSpecification(string catalogItemName)
    {
        Query.Where(item => catalogItemName == item.Name);
    }
}
