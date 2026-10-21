using System;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.CatalogService.Domain.Entities;

namespace Microsoft.eShopWeb.CatalogService.Domain.Specifications;

public class CatalogItemsSpecification : Specification<CatalogItem>
{
    public CatalogItemsSpecification(params int[] ids)
    {
        Query.Where(c => ids.Contains(c.Id));
    }
}
