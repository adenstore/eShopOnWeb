using System;

namespace Microsoft.eShopWeb.CatalogService.Domain.Exceptions;

public class DuplicateException : Exception
{
    public DuplicateException(string message) : base(message)
    {

    }

}
