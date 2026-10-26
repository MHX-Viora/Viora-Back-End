using System.Security.Claims;
using OpenIddict.Abstractions;
using Viora.Infrastructure.Developers;

namespace Viora.Infrastructure.Sso;

public static class SsoProfileClaims
{
    public static readonly string[] Public = ["sub", "name", "preferred_username", "nickname", "gender", "birthdate", "picture", "ankt_cover_picture", "email", "email_verified",
        "id", "username", "displayName", "fullName", "avatarUrl", "coverUrl", "isSandbox"];
    public static void Destinations(ClaimsPrincipal principal) => principal.SetDestinations(claim =>
        claim.Type == SandboxTestService.SessionClaim ? [OpenIddictConstants.Destinations.AccessToken] :
        Public.Contains(claim.Type, StringComparer.Ordinal) ?
            [OpenIddictConstants.Destinations.AccessToken, OpenIddictConstants.Destinations.IdentityToken] : Array.Empty<string>());
}
