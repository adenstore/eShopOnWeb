using System;
using Microsoft.eShopWeb.CatalogService.Contracts;

namespace Microsoft.eShopWeb.CatalogService.Api.Endpoints.CatalogItemEndpoints;

public class UpdateCatalogItemResponse : BaseResponse
{
    public UpdateCatalogItemResponse(Guid correlationId) : base(correlationId)
    {
    }

    public UpdateCatalogItemResponse()
    {
    }

    public CatalogItemDto CatalogItem { get; set; }
}
