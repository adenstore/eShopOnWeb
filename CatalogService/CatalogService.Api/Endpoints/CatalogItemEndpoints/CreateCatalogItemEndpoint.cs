using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.CatalogService.Api.Authorization;
using Microsoft.eShopWeb.CatalogService.Domain.Entities;
using Microsoft.eShopWeb.CatalogService.Domain.Exceptions;
using Microsoft.eShopWeb.CatalogService.Domain.Interfaces;
using Microsoft.eShopWeb.CatalogService.Domain.Specifications;
using MinimalApi.Endpoint;
using Microsoft.eShopWeb.CatalogService.Contracts;
using Microsoft.eShopWeb.CatalogService.Contracts.Events;
using Microsoft.eShopWeb.CatalogService.Infrastructure.Messaging;

namespace Microsoft.eShopWeb.CatalogService.Api.Endpoints.CatalogItemEndpoints;

/// <summary>
/// Creates a new Catalog Item
/// </summary>
public class CreateCatalogItemEndpoint : IEndpoint<IResult, CreateCatalogItemRequest, IRepository<CatalogItem>>
{
    private readonly IUriComposer _uriComposer;
    private readonly ICatalogEventPublisher _eventPublisher;

    public CreateCatalogItemEndpoint(IUriComposer uriComposer, ICatalogEventPublisher eventPublisher)
    {
        _uriComposer = uriComposer;
        _eventPublisher = eventPublisher;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/catalog-items",
            [Authorize(Roles = AuthorizationConstants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateCatalogItemRequest request, IRepository<CatalogItem> itemRepository) =>
            {
                return await HandleAsync(request, itemRepository);
            })
            .Produces<CreateCatalogItemResponse>()
            .WithTags("CatalogItemEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateCatalogItemRequest request, IRepository<CatalogItem> itemRepository)
    {
        var response = new CreateCatalogItemResponse(request.CorrelationId());

        var catalogItemNameSpecification = new CatalogItemNameSpecification(request.Name);
        var existingCataloogItem = await itemRepository.CountAsync(catalogItemNameSpecification);
        if (existingCataloogItem > 0)
        {
            throw new DuplicateException($"A catalogItem with name {request.Name} already exists");
        }

        var newItem = new CatalogItem(request.CatalogTypeId, request.CatalogBrandId, request.Description, request.Name, request.Price, request.PictureUri);
        newItem = await itemRepository.AddAsync(newItem);

        if (newItem.Id != 0)
        {
            //We disabled the upload functionality and added a default/placeholder image to this sample due to a potential security risk 
            //  pointed out by the community. More info in this issue: https://github.com/dotnet-architecture/eShopOnWeb/issues/537 
            //  In production, we recommend uploading to a blob storage and deliver the image via CDN after a verification process.

            newItem.UpdatePictureUri("eCatalog-item-default.png");
            await itemRepository.UpdateAsync(newItem);
        }

        await _eventPublisher.PublishAsync(new CatalogItemCreatedIntegrationEvent(
            Guid.NewGuid(), DateTimeOffset.UtcNow, newItem.Id, newItem.Name, newItem.Description, newItem.Price,
            newItem.PictureUri, newItem.CatalogBrandId, newItem.CatalogTypeId));

        var dto = new CatalogItemDto
        {
            Id = newItem.Id,
            CatalogBrandId = newItem.CatalogBrandId,
            CatalogTypeId = newItem.CatalogTypeId,
            Description = newItem.Description,
            Name = newItem.Name,
            PictureUri = _uriComposer.ComposePicUri(newItem.PictureUri),
            Price = newItem.Price
        };
        response.CatalogItem = dto;
        return Results.Created($"api/catalog-items/{dto.Id}", response);
    }
}
