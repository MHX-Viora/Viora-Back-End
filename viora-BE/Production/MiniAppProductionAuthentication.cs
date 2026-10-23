using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Viora.Application.Developers;
using Viora.Infrastructure.Developers;

namespace viora_BE.Production;

public static class MiniAppProductionAuthentication
{
    public static void RequireWebOrigin(HttpContext http, MiniAppProductionOptions options)
    {
        if (!http.Request.IsHttps || http.Request.Headers.Origin.Count != 1 ||
            http.Request.Headers.Origin.ToString() != options.FirstPartyOrigin)
            throw new DeveloperAuthException(403, "HOST_ORIGIN_DENIED", "Không thể mở Mini App từ nguồn này.");
    }

    public static async Task<MiniAppProductionActor> ResolveAsync(HttpContext http, MiniAppProductionAccess access, CancellationToken ct)
    {
        var headers = http.Request.Headers.Authorization;
        if (headers.Count == 0) return MiniAppProductionActor.Anonymous;
        if (headers.Count != 1 || !System.Net.Http.Headers.AuthenticationHeaderValue.TryParse(headers.ToString(), out var supplied) ||
            !supplied.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(supplied.Parameter))
            throw MiniAppProductionAccess.Invalid();
        // Authenticate the configured signed Social access token; a cookie or decoded
        // payload cannot turn a supplied invalid bearer into an anonymous launch.
        var result = await http.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
        var principal = result.Principal;
        if (!result.Succeeded || principal?.Identity?.IsAuthenticated != true || principal.FindFirst("token_type")?.Value != "access" ||
            !Guid.TryParse(principal.FindFirst("sub")?.Value, out var account) || !Guid.TryParse(principal.FindFirst("sid")?.Value, out var source))
            throw MiniAppProductionAccess.Invalid();
        var actor = new MiniAppProductionActor(account, source); await access.ValidateAsync(actor, ct); return actor;
    }
}
