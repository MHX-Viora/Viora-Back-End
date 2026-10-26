using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Developers;

// Partner grants reuse Bridge storage, but bind to a parent session UUID instead
// of a PKCE challenge. A 36-character UUID can never match an S256 challenge.
public sealed class MiniAppPartnerLoginService(AppDbContext db, MiniAppBridgeService bridge,
    IOptions<MiniAppBridgeOptions> options, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static DeveloperAuthException InvalidCode() => new(400, "INVALID_LOGIN_CODE", "Mã đăng nhập không hợp lệ hoặc đã hết hạn.");
    private void Credentials(string client, string secret, bool sandbox)
    {
        var registered = options.Value.Partner(client) ?? throw InvalidClient();
        var hash = sandbox ? registered.SandboxSecretHash : registered.ProductionSecretHash;
        if (secret.Length is < 32 or > 256 || hash.Length != 64 ||
            !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(MiniAppBridgeService.Hash(secret)), Encoding.ASCII.GetBytes(hash.ToUpperInvariant())))
            throw InvalidClient();
    }
    private static DeveloperAuthException InvalidClient() => new(401, "INVALID_CLIENT", "Thông tin backend Mini App không hợp lệ.");

    public async Task<MiniAppLoginCode> IssueAsync(string token, string origin, string client, CancellationToken ct)
    {
        var parent = await bridge.AuthenticateAsync(token, origin, ct, client);
        await bridge.ProfileAsync(parent, ct);
        if (options.Value.Partner(client) is not { } registered ||
            (parent.IsSandbox ? registered.SandboxSecretHash : registered.ProductionSecretHash).Length != 64)
            throw new DeveloperAuthException(503, "PARTNER_LOGIN_NOT_CONFIGURED", "Backend Mini App chưa cấu hình đăng nhập ANKT.");
        var parentId = parent.Id.ToString("D");
        if (await db.Set<MiniAppBridgeSession>().CountAsync(x => x.CodeChallenge == parentId && x.ConsumedAt == null && x.CodeExpiresAt > Now && x.RevokedAt == null, ct) >= 5)
            throw new DeveloperAuthException(429, "SESSION_LIMIT", "Vui lòng thử lại sau.");
        var code = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        db.Add(new MiniAppBridgeSession {
            MiniAppId = parent.MiniAppId, AccountId = parent.AccountId, ActorId = parent.ActorId,
            SourceSessionId = parent.SourceSessionId, IsSandbox = parent.IsSandbox, ClientId = client,
            Origin = parent.Origin, Policy = parent.Policy, CodeHash = MiniAppBridgeService.Hash(code),
            CodeChallenge = parentId, CodeExpiresAt = Now.AddSeconds(60), ExpiresAt = parent.ExpiresAt
        });
        await db.SaveChangesAsync(ct);
        return new(code, 60);
    }

    public async Task<MiniAppPartnerIdentity> ExchangeAsync(string client, string secret, string? code, CancellationToken ct)
    {
        if (code is not { Length: 43 } || code.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_'))) throw InvalidCode();
        var hash = MiniAppBridgeService.Hash(code);
        var grant = await db.Set<MiniAppBridgeSession>().AsNoTracking().SingleOrDefaultAsync(x =>
            x.CodeHash == hash && x.CodeChallenge.Length == 36 && x.ConsumedAt == null && x.CodeExpiresAt > Now, ct) ?? throw InvalidCode();
        Credentials(client, secret, grant.IsSandbox);
        if (grant.ClientId != client) throw InvalidClient();
        var identity = await IdentityAsync(grant, ct);
        var now = Now;
        if (await db.Set<MiniAppBridgeSession>().Where(x => x.Id == grant.Id && x.ConsumedAt == null && x.CodeExpiresAt > now && x.RevokedAt == null)
            .ExecuteUpdateAsync(x => x.SetProperty(r => r.ConsumedAt, now), ct) != 1) throw InvalidCode();
        return identity;
    }

    public async Task<MiniAppPartnerIdentity> ValidateAsync(string client, string secret, Guid id, CancellationToken ct)
    {
        var grant = await db.Set<MiniAppBridgeSession>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.CodeChallenge.Length == 36 && x.ConsumedAt != null, ct)
            ?? throw new DeveloperAuthException(401, "SESSION_EXPIRED", "Phiên Mini App đã hết hạn.");
        Credentials(client, secret, grant.IsSandbox);
        if (grant.ClientId != client) throw InvalidClient();
        return await IdentityAsync(grant, ct);
    }

    private async Task<MiniAppPartnerIdentity> IdentityAsync(MiniAppBridgeSession grant, CancellationToken ct)
    {
        if (grant.RevokedAt != null || grant.ExpiresAt <= Now || !Guid.TryParseExact(grant.CodeChallenge, "D", out var parentId))
            throw new DeveloperAuthException(401, "SESSION_EXPIRED", "Phiên Mini App đã hết hạn.");
        var parent = await bridge.ValidatePartnerSourceAsync(parentId, ct);
        if (parent.ClientId != grant.ClientId || parent.AccountId != grant.AccountId || parent.IsSandbox != grant.IsSandbox || parent.Policy != grant.Policy)
            throw InvalidCode();
        var user = await bridge.ProfileAsync(parent, ct);
        return new(new(user.Id, user.DisplayName, user.AvatarUrl, user.CoverUrl, user.Gender), grant.ClientId,
            grant.IsSandbox ? "Sandbox" : "Production", grant.Id, grant.ExpiresAt);
    }
}
