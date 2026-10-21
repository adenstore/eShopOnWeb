using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.CatalogService.Contracts;

namespace Microsoft.eShopWeb.CatalogService.Api.Endpoints.CatalogItemEndpoints;

public class ListPagedCatalogItemResponse : BaseResponse
{
    public ListPagedCatalogItemResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListPagedCatalogItemResponse()
    {
    }

    public List<CatalogItemDto> CatalogItems { get; set; } = new List<CatalogItemDto>();
    public int PageCount { get; set; }
    public int TotalItems { get; set; }
}
