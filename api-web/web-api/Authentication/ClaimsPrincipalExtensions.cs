using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace web_api.Authentication;

public static class ClaimsPrincipalExtensions
{
    public static bool TryGetUserId(this ClaimsPrincipal principal, out Guid userId)
    {
        return Guid.TryParse(
            principal.FindFirstValue(JwtRegisteredClaimNames.Sub),
            out userId);
    }
}
