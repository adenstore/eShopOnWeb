using Microsoft.eShopWeb.CatalogService.Domain.Entities;

namespace Microsoft.eShopWeb.CatalogService.Tests.Builders;

public class CatalogItemBuilder
{
    private int _id;
    private int _catalogTypeId = 1;
    private int _catalogBrandId = 1;
    private string _name = "Test item";
    private string _description = "Test description";
    private decimal _price = 1.23m;
    private string _pictureUri = "TestUri";

    public CatalogItemBuilder WithId(int id) { _id = id; return this; }
    public CatalogItemBuilder WithBrand(int catalogBrandId) { _catalogBrandId = catalogBrandId; return this; }
    public CatalogItemBuilder WithType(int catalogTypeId) { _catalogTypeId = catalogTypeId; return this; }
    public CatalogItemBuilder WithName(string name) { _name = name; return this; }

    public CatalogItem Build() =>
        new CatalogItemWithId(_id, _catalogTypeId, _catalogBrandId, _description, _name, _price, _pictureUri);

    // Id has a protected setter (normally assigned by the database), so tests set it through a subclass.
    private class CatalogItemWithId : CatalogItem
    {
        public CatalogItemWithId(int id, int catalogTypeId, int catalogBrandId, string description, string name, decimal price, string pictureUri)
            : base(catalogTypeId, catalogBrandId, description, name, price, pictureUri)
        {
            Id = id;
        }
    }
}
