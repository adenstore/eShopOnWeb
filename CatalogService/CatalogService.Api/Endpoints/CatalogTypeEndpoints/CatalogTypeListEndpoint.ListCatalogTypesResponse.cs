using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.CatalogService.Contracts;

namespace Microsoft.eShopWeb.CatalogService.Api.Endpoints.CatalogTypeEndpoints;

public class ListCatalogTypesResponse : BaseResponse
{
    public ListCatalogTypesResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListCatalogTypesResponse()
    {
    }

    public List<CatalogTypeDto> CatalogTypes { get; set; } = new List<CatalogTypeDto>();
}
