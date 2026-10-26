using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Developers;

public sealed class MiniAppBridgeOptions
{
    public bool Enabled { get; set; }
    public string AllowedHostOrigin { get; set; } = "https://mxh.ankt.vn";
}

public sealed class MiniAppBridgeService(AppDbContext db, SandboxTestService sandbox,
    IOptions<MiniAppBridgeOptions> options, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static string Challenge(string value) => WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(value)));
    private static string Random() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    private static bool Credential(string? value) => value is { Length: 43 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    private static DeveloperAuthException Error(string code, int status = 403) => new(status, code, "Không thể xác minh phiên Mini App. Kiểm tra cấu hình hoặc mở lại từ ANKT.");
    private void Enabled() { if (!options.Value.Enabled) throw Error("HOST_UNAVAILABLE", 503); }

    private async Task<(MiniApp App, string Url, string Policy)> Target(string client, MiniAppBridgeActor? actor, CancellationToken ct)
    {
        Enabled();
        var app = await db.MiniApps.AsNoTracking().Include(x => x.Developer)
            .SingleOrDefaultAsync(x => x.AppId == client && x.DeletedAt == null, ct) ?? throw Error("INVALID_CLIENT", 404);
        string? url; string policy;
        if (actor?.IsSandbox == true)
        {
            if (!sandbox.Enabled) throw Error("SANDBOX_NOT_ALLOWED");
            try { await sandbox.OwnerAsync(actor.Id, actor.SourceSessionId, app.AppId, ct); }
            catch (DeveloperAuthException e) { throw Error(e.Status == 404 ? "SANDBOX_NOT_ALLOWED" : "SESSION_EXPIRED", e.Status == 404 ? 403 : 401); }
            url = app.WebsiteUrl; policy = app.Version.ToString();
        }
        else
        {
            MiniAppProductionTarget target;
            try { target = await MiniAppProductionEligibility.PublicationAsync(db, app.Id, Now, ct); }
            catch (DeveloperAuthException) { throw Error("MINIAPP_NOT_APPROVED", 404); }
            url = target.Snapshot.WebsiteUrl;
            var operational = await db.MiniAppOperationalStates.Where(x => x.MiniAppId == app.Id).Select(x => x.ConcurrencyToken).SingleAsync(ct);
            policy = target.Publication.ConcurrencyToken + ":" + operational + ":" + target.Version.SnapshotHash;
        }
        if (!Uri.TryCreate(url, UriKind.Absolute, out var site) || site.Scheme != "https" || site.UserInfo.Length != 0 ||
            site.Fragment.Length != 0 || site.GetLeftPart(UriPartial.Authority) == options.Value.AllowedHostOrigin ||
            !await db.MiniAppDomainVerifications.AsNoTracking().AnyAsync(x => x.MiniAppId == app.Id && x.Hostname == site.IdnHost &&
                x.Status == MiniAppDomainStatus.Verified && x.RevokedAt == null && x.ExpiresAt > Now, ct)) throw Error("INVALID_ORIGIN");
        var proof = await db.MiniAppDomainVerifications.AsNoTracking().Where(x => x.MiniAppId == app.Id && x.Hostname == site.IdnHost &&
            x.Status == MiniAppDomainStatus.Verified && x.RevokedAt == null && x.ExpiresAt > Now).Select(x => x.Version).FirstAsync(ct);
        policy += ":" + proof;
        return (app, site.AbsoluteUri, policy);
    }

    public async Task<MiniAppBridgeConfig> ConfigAsync(string client, MiniAppBridgeActor? actor, CancellationToken ct)
    {
        var target = await Target(client, actor, ct);
        return new(client, target.Url, new Uri(target.Url).GetLeftPart(UriPartial.Authority), actor?.IsSandbox == true ? "Sandbox" : "Production", ["profile"]);
    }

    public async Task<MiniAppBridgeLaunch> LaunchAsync(MiniAppBridgeActor actor, MiniAppBridgeLaunchRequest request, CancellationToken ct)
    {
        if (request.Scope != "profile") throw Error("INVALID_SCOPE", 400);
        if (!Credential(request.CodeChallenge)) throw Error("INVALID_PROOF", 400);
        var target = await Target(request.ClientId, actor, ct);
        var origin = new Uri(target.Url).GetLeftPart(UriPartial.Authority);
        if (origin != request.Origin) throw Error("INVALID_ORIGIN");
        Guid account;
        if (actor.IsSandbox) account = (await sandbox.BridgeIdentityAsync(ct)).AccountId;
        else
        {
            try { await MiniAppHostSessionValidator.ValidateAsync(db, clock, actor.Id, actor.SourceSessionId, ct); }
            catch (DeveloperAuthException) { throw Error("SESSION_EXPIRED", 401); }
            account = actor.Id;
        }
        if (await db.Set<MiniAppBridgeSession>().CountAsync(x => x.ActorId == actor.Id && x.SourceSessionId == actor.SourceSessionId &&
            x.ExpiresAt > Now && x.RevokedAt == null, ct) >= 10) throw Error("SESSION_LIMIT", 429);
        var code = Random(); var session = new MiniAppBridgeSession
        {
            MiniAppId = target.App.Id, AccountId = account, ActorId = actor.Id, SourceSessionId = actor.SourceSessionId,
            IsSandbox = actor.IsSandbox, ClientId = request.ClientId, Origin = origin, Policy = target.Policy,
            CodeHash = Hash(code), CodeChallenge = request.CodeChallenge, CodeExpiresAt = Now.AddSeconds(60), ExpiresAt = Now.AddMinutes(15)
        };
        db.Add(session); await db.SaveChangesAsync(ct);
        return new(session.Id, code, 60, session.ClientId, origin, session.IsSandbox ? "Sandbox" : "Production");
    }

    private async Task RequireAsync(MiniAppBridgeSession session, CancellationToken ct)
    {
        if (session.RevokedAt != null || session.ExpiresAt <= Now) throw Error("SESSION_EXPIRED", 401);
        var actor = new MiniAppBridgeActor(session.ActorId, session.SourceSessionId, session.IsSandbox);
        var target = await Target(session.ClientId, actor, ct);
        if (target.Policy != session.Policy || target.App.Id != session.MiniAppId ||
            new Uri(target.Url).GetLeftPart(UriPartial.Authority) != session.Origin) throw Error("SESSION_EXPIRED", 401);
        if (session.IsSandbox)
        {
            if ((await sandbox.BridgeIdentityAsync(ct)).AccountId != session.AccountId) throw Error("SESSION_EXPIRED", 401);
        }
        else
        {
            if (session.ActorId != session.AccountId) throw Error("SESSION_EXPIRED", 401);
            try { await MiniAppHostSessionValidator.ValidateAsync(db, clock, session.AccountId, session.SourceSessionId, ct); }
            catch (DeveloperAuthException) { throw Error("SESSION_EXPIRED", 401); }
        }
    }

    public async Task<MiniAppBridgeToken> ExchangeAsync(MiniAppBridgeExchange request, string origin, CancellationToken ct)
    {
        Enabled();
        if (!Credential(request.Code) || request.CodeVerifier is not { Length: >= 43 and <= 128 } ||
            request.CodeVerifier.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.' or '~'))) throw Error("AUTH_EXCHANGE_FAILED", 400);
        var hash = Hash(request.Code);
        var session = await db.Set<MiniAppBridgeSession>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.SessionId &&
            x.CodeHash == hash && x.ClientId == request.ClientId && x.ConsumedAt == null && x.CodeExpiresAt > Now, ct) ?? throw Error("AUTH_EXCHANGE_FAILED", 400);
        if (origin != session.Origin) throw Error("INVALID_ORIGIN");
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Challenge(request.CodeVerifier)), Encoding.ASCII.GetBytes(session.CodeChallenge)))
            throw Error("AUTH_EXCHANGE_FAILED", 400);
        await RequireAsync(session, ct);
        var raw = (session.IsSandbox ? "mas_" : "map_") + Random(); var accessHash = Hash(raw); var now = Now;
        if (await db.Set<MiniAppBridgeSession>().Where(x => x.Id == session.Id && x.ConsumedAt == null && x.CodeExpiresAt > now && x.RevokedAt == null)
            .ExecuteUpdateAsync(x => x.SetProperty(s => s.ConsumedAt, now).SetProperty(s => s.AccessHash, accessHash)
                .SetProperty(s => s.AccessExpiresAt, now.AddMinutes(5)), ct) != 1) throw Error("AUTH_EXCHANGE_FAILED", 400);
        return new(raw, "Bearer", 300, "profile", session.Id, session.IsSandbox ? "Sandbox" : "Production");
    }

    public async Task<MiniAppBridgeSession> AuthenticateAsync(string? token, string origin, CancellationToken ct, string clientId)
    {
        Enabled();
        if (token is not { Length: 47 } || !(token.StartsWith("mas_", StringComparison.Ordinal) || token.StartsWith("map_", StringComparison.Ordinal)) ||
            !Credential(token[4..])) throw Error("SESSION_EXPIRED", 401);
        var hash = Hash(token); var session = await db.Set<MiniAppBridgeSession>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.AccessHash == hash && x.AccessExpiresAt > Now && x.ConsumedAt != null, ct) ?? throw Error("SESSION_EXPIRED", 401);
        if (session.ClientId != clientId) throw Error("INVALID_CLIENT");
        if (session.Origin != origin || session.IsSandbox != token.StartsWith("mas_", StringComparison.Ordinal)) throw Error("INVALID_ORIGIN");
        await RequireAsync(session, ct); return session;
    }

    public async Task<MiniAppBridgeUser> MeAsync(string? token, string origin, CancellationToken ct, string clientId)
    {
        var session = await AuthenticateAsync(token, origin, ct, clientId);
        var account = await db.Accounts.IgnoreQueryFilters().AsNoTracking().Include(x => x.User)
            .SingleOrDefaultAsync(x => x.Id == session.AccountId && x.IsSandbox == session.IsSandbox && x.Status == AccountStatus.Active && x.DeletedAt == null, ct);
        if (account?.User == null) throw Error("SESSION_EXPIRED", 401);
        var template = session.IsSandbox ? await sandbox.BridgeIdentityAsync(ct) : null;
        return new(account.User.Id, template?.Nickname ?? account.User.DisplayName, account.User.DisplayName,
            account.User.AvatarUrl, account.User.CoverUrl, account.User.Gender.ToString(), session.IsSandbox, template?.FullName);
    }

    public async Task RevokeAsync(Guid id, MiniAppBridgeActor actor, CancellationToken ct, string? clientId = null)
    {
        Enabled();
        if (await db.Set<MiniAppBridgeSession>().Where(x => x.Id == id && x.ActorId == actor.Id && x.SourceSessionId == actor.SourceSessionId &&
            x.IsSandbox == actor.IsSandbox && (clientId == null || x.ClientId == clientId)).ExecuteUpdateAsync(x => x.SetProperty(s => s.RevokedAt, Now), ct) != 1) throw Error("SESSION_EXPIRED", 404);
    }
    public Task RevokeTokenAsync(Guid id, CancellationToken ct) => db.Set<MiniAppBridgeSession>().Where(x => x.Id == id)
        .ExecuteUpdateAsync(x => x.SetProperty(s => s.RevokedAt, Now), ct);
}
