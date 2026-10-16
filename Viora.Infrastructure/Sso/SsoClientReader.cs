using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Sso;

public sealed class SsoClientReader(AppDbContext db, TimeProvider clock)
{
    public async Task EnsureBrowserOriginAsync(MiniAppOAuthClient client, string? origin, CancellationToken ct)
    {
        if (origin == null) return; // Backend calls do not carry a browser Origin.
        if (client.ClientType != "Public" || !Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            origin != uri.GetLeftPart(UriPartial.Authority)) throw OriginDenied();
        var redirects = await db.MiniAppRedirectUris.AsNoTracking().Where(x => x.MiniAppId == client.MiniAppId && x.Environment == client.Environment)
            .Select(x => x.RedirectUri).ToListAsync(ct);
        if (!redirects.Any(x => Uri.TryCreate(x, UriKind.Absolute, out var redirect) && redirect.GetLeftPart(UriPartial.Authority) == origin)) throw OriginDenied();
    }

    private static DeveloperAuthException OriginDenied() => new(403, "SSO_ORIGIN_DENIED", "Origin không được phép sử dụng client này.");

    public async Task<MiniAppOAuthClient> EligibleAsync(string? clientId, IEnumerable<string> scopes, string? environment, CancellationToken ct)
    {
        var client = await db.MiniAppOAuthClients.AsNoTracking().Include(x => x.MiniApp).ThenInclude(x => x.Developer)
            .SingleOrDefaultAsync(x => x.ClientKey == clientId, ct);
        if (client == null || client.ClientType is not ("Public" or "Confidential"))
            throw new DeveloperAuthException(403, "SSO_CLIENT_UNAVAILABLE", "Ứng dụng không khả dụng.");
        var proof = await db.MiniAppDomainVerifications.AsNoTracking().SingleOrDefaultAsync(x => x.MiniAppId == client.MiniAppId && x.RevokedAt == null, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        SsoClientPolicy.EnsureEligible(client, client.MiniApp, proof, scopes, environment ?? client.Environment.ToString(), now);
        if (client.ClientType == "Confidential" && !await db.MiniAppClientSecrets.AnyAsync(x => x.OAuthClientId == client.Id && x.RevokedAt == null && (x.ExpiresAt == null || x.ExpiresAt > now), ct))
            throw new DeveloperAuthException(403, "SSO_CLIENT_UNAVAILABLE", "Thông tin xác thực ứng dụng đã hết hạn.");
        return client;
    }
}
