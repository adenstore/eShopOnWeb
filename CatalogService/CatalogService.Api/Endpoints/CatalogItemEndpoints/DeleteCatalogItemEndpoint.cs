using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.CatalogService.Api.Authorization;
using Microsoft.eShopWeb.CatalogService.Domain.Entities;
using Microsoft.eShopWeb.CatalogService.Domain.Interfaces;
using MinimalApi.Endpoint;
using Microsoft.eShopWeb.CatalogService.Contracts.Events;
using Microsoft.eShopWeb.CatalogService.Infrastructure.Messaging;

namespace Microsoft.eShopWeb.CatalogService.Api.Endpoints.CatalogItemEndpoints;

/// <summary>
/// Deletes a Catalog Item
/// </summary>
public class DeleteCatalogItemEndpoint : IEndpoint<IResult, DeleteCatalogItemRequest, IRepository<CatalogItem>>
{
    private readonly ICatalogEventPublisher _eventPublisher;

    public DeleteCatalogItemEndpoint(ICatalogEventPublisher eventPublisher)
    {
        _eventPublisher = eventPublisher;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/catalog-items/{catalogItemId}",
            [Authorize(Roles = AuthorizationConstants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int catalogItemId, IRepository<CatalogItem> itemRepository) =>
            {
                return await HandleAsync(new DeleteCatalogItemRequest(catalogItemId), itemRepository);
            })
            .Produces<DeleteCatalogItemResponse>()
            .WithTags("CatalogItemEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteCatalogItemRequest request, IRepository<CatalogItem> itemRepository)
    {
        var response = new DeleteCatalogItemResponse(request.CorrelationId());

        var itemToDelete = await itemRepository.GetByIdAsync(request.CatalogItemId);
        if (itemToDelete is null)
            return Results.NotFound();

        await itemRepository.DeleteAsync(itemToDelete);

        await _eventPublisher.PublishAsync(new CatalogItemDeletedIntegrationEvent(
            Guid.NewGuid(), DateTimeOffset.UtcNow, request.CatalogItemId));

        return Results.Ok(response);
    }
}
