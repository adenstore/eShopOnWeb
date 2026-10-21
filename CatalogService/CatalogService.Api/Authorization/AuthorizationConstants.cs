namespace Microsoft.eShopWeb.CatalogService.Api.Authorization;

// Must match the monolith's AuthorizationConstants and BlazorShared roles, so tokens
// issued by PublicApi's authenticate endpoint are accepted here as well.
public static class AuthorizationConstants
{
    // TODO: Change this to an environment variable
    public const string JWT_SECRET_KEY = "SecretKeyOfDoomThatMustBeAMinimumNumberOfBytes";

    public static class Roles
    {
        public const string ADMINISTRATORS = "Administrators";
    }
}
