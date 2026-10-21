using AutoMapper;
using Microsoft.eShopWeb.CatalogService.Domain.Entities;
using Microsoft.eShopWeb.CatalogService.Api.Endpoints.CatalogBrandEndpoints;
using Microsoft.eShopWeb.CatalogService.Api.Endpoints.CatalogItemEndpoints;
using Microsoft.eShopWeb.CatalogService.Api.Endpoints.CatalogTypeEndpoints;

namespace Microsoft.eShopWeb.CatalogService.Api;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<CatalogItem, CatalogItemDto>();
        CreateMap<CatalogType, CatalogTypeDto>()
            .ForMember(dto => dto.Name, options => options.MapFrom(src => src.Type));
        CreateMap<CatalogBrand, CatalogBrandDto>()
            .ForMember(dto => dto.Name, options => options.MapFrom(src => src.Brand));
    }
}
