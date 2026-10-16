using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Sso;

public sealed class SsoClientReader(AppDbContext db, TimeProvider clock)
{
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
