using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.eShopWeb.CatalogService.Api.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace Microsoft.eShopWeb.CatalogService.Tests.Endpoints;

/// <summary>
/// Creates JWTs signed with the same key the monolith uses, like the tokens BlazorAdmin sends.
/// </summary>
public static class ApiTokenHelper
{
    public static AuthenticationHeaderValue AdminUser() =>
        Bearer("admin@microsoft.com", AuthorizationConstants.Roles.ADMINISTRATORS);

    public static AuthenticationHeaderValue NormalUser() =>
        Bearer("demouser@microsoft.com");

    private static AuthenticationHeaderValue Bearer(string userName, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, userName) };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var key = Encoding.ASCII.GetBytes(AuthorizationConstants.JWT_SECRET_KEY);
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };
        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.WriteToken(tokenHandler.CreateToken(tokenDescriptor));

        return new AuthenticationHeaderValue("Bearer", token);
    }
}
