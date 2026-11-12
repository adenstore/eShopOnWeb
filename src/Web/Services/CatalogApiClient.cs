using System.Net.Http.Json;
using Microsoft.eShopWeb.CatalogService.Contracts;
using Microsoft.eShopWeb.Web.Interfaces;

namespace Microsoft.eShopWeb.Web.Services;

/// <summary>
/// Talks to the Catalog microservice over HTTP. The HttpClient's BaseAddress is set
/// from the "CatalogServiceBaseUrl" setting when it is registered.
/// </summary>
public class CatalogApiClient : ICatalogApiClient
{
    private readonly HttpClient _httpClient;

    public CatalogApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<CatalogItemsPage> ListCatalogItemsAsync(int pageIndex, int pageSize, int? brandId, int? typeId, CancellationToken cancellationToken = default)
    {
        var query = $"api/catalog-items?pageIndex={pageIndex}&pageSize={pageSize}";
        if (brandId.HasValue)
        {
            query += $"&catalogBrandId={brandId}";
        }
        if (typeId.HasValue)
        {
            query += $"&catalogTypeId={typeId}";
        }

        var response = await _httpClient.GetFromJsonAsync<ListPagedCatalogItemsResponse>(query, cancellationToken);
        return new CatalogItemsPage(response?.CatalogItems ?? new List<CatalogItemDto>(), response?.TotalItems ?? 0);
    }

    public async Task<IReadOnlyList<CatalogBrandDto>> ListBrandsAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetFromJsonAsync<ListCatalogBrandsResponse>("api/catalog-brands", cancellationToken);
        return response?.CatalogBrands ?? new List<CatalogBrandDto>();
    }

    public async Task<IReadOnlyList<CatalogTypeDto>> ListTypesAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetFromJsonAsync<ListCatalogTypesResponse>("api/catalog-types", cancellationToken);
        return response?.CatalogTypes ?? new List<CatalogTypeDto>();
    }

    public async Task<CatalogItemDto?> GetCatalogItemAsync(int catalogItemId, CancellationToken cancellationToken = default)
    {
        // CatalogService answers 404 for an unknown id; that is turned into null instead of an exception.
        var response = await _httpClient.GetAsync($"api/catalog-items/{catalogItemId}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<GetCatalogItemResponse>(cancellationToken);
        return body?.CatalogItem;
    }

    // Only the parts of CatalogService's responses that the Web app reads.
    private record GetCatalogItemResponse(CatalogItemDto CatalogItem);
    private record ListPagedCatalogItemsResponse(List<CatalogItemDto> CatalogItems, int TotalItems);
    private record ListCatalogBrandsResponse(List<CatalogBrandDto> CatalogBrands);
    private record ListCatalogTypesResponse(List<CatalogTypeDto> CatalogTypes);
}
