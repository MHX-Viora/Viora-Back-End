using System.Security.Claims;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using Viora.Application.MiniApps;
using Viora.Domain.Entities;
using Viora.Infrastructure.MiniApps;
using Viora.Infrastructure.Persistence;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Viora.Infrastructure.OpenApi;

public sealed record OpenApiTransaction(string ClientId, string RedirectUri, string State, string CodeChallenge, string CodeChallengeMethod, string Nonce, string[] Scopes, Guid? SessionId = null, string? SessionToken = null);
public sealed record OpenApiDecision(Guid AppId, string Name, string? IconUrl, bool RequiresConsent, IReadOnlyList<MiniAppPermissionDto> Permissions, string? Ticket);
public sealed record OpenApiIdentity(MiniApp App, Account Account, MiniAppExternalIdentity Link, string[] Scopes);

public sealed class OpenApiAccessService(AppDbContext db, IClientCredentialService credentials, IOpenIddictTokenManager tokens, IOpenIddictApplicationManager applications, IMiniAppService runtime, TimeProvider clock)
{
    public async Task<MiniApp?> FindApp(string clientId, CancellationToken ct) => await db.MiniApps.Include(a => a.Developer).Include(a => a.PermissionMappings).ThenInclude(m => m.Permission)
        .SingleOrDefaultAsync(a => a.ClientId == clientId && a.DeletedAt == null && a.Status == MiniAppStatus.Active && a.Developer.Status == DeveloperStatus.Active && a.PublishedVersion > 0 && a.PublishedConfigurationJson != null, ct);

    public async Task<OpenApiDecision> Prepare(Guid accountId, OpenApiTransaction input, CancellationToken ct)
    {
        var app = await FindApp(input.ClientId, ct) ?? throw new MiniAppException("INVALID_CLIENT", "Ứng dụng không hoạt động.", 403);
        var config = MiniAppConfigurationSnapshot.Published(app);
        if (config.AuthenticationMode != "AnktSso") throw new MiniAppException("SSO_DISABLED", "Ứng dụng sử dụng đăng nhập riêng.", 403);
        if (!OpenApiPolicy.ValidTransaction(input.RedirectUri, config.CallbackUrls ?? [], input.State ?? "", input.CodeChallenge ?? "", input.CodeChallengeMethod ?? "", input.Nonce ?? ""))
            throw new MiniAppException("INVALID_REQUEST", "Callback, state, nonce hoặc PKCE không hợp lệ.");
        if (input.Scopes is null || input.Scopes.Length is < 1 or > 4 || !input.Scopes.Contains("openid") || input.Scopes.Distinct().Count() != input.Scopes.Length) throw new MiniAppException("INVALID_SCOPE", "Yêu cầu scope openid và tối đa bốn scope.");
        string[] permissions;
        try { permissions = OpenApiPolicy.PermissionsFor(input.Scopes); } catch (ArgumentException) { throw new MiniAppException("INVALID_SCOPE", "Scope không được hỗ trợ."); }
        if (!await db.Accounts.AnyAsync(a => a.Id == accountId && a.Status == AccountStatus.Active && a.DeletedAt == null, ct)) throw new MiniAppException("ACCOUNT_INACTIVE", "Phiên tài khoản không còn hợp lệ.", 403);
        if (input.SessionId is Guid sessionId)
        {
            var validation = await runtime.ValidateSessionAsync(accountId, app.Id, new(sessionId, input.SessionToken ?? ""), ct);
            if (!validation.Active) throw new MiniAppException("SESSION_EXPIRED", "Phiên Mini App đã hết hạn.", 403);
        }
        var host = new Uri(input.RedirectUri).IdnHost;
        if (!await db.MiniAppVerifiedDomains.AnyAsync(d => d.MiniAppId == app.Id && d.Host == host && d.VerifiedAt != null, ct)) throw new MiniAppException("DOMAIN_NOT_VERIFIED", "Callback chưa được xác minh.", 403);
        var approved = await db.MiniAppPermissions.Where(p => permissions.Contains(p.Code) && config.Permissions.Contains(p.Code) && p.Status == MiniAppPermissionStatus.Active)
            .Select(p => new MiniAppPermissionDto(p.Code, p.Name, p.Description, p.IsSensitive, p.Code == "identity.login")).ToListAsync(ct);
        if (approved.Count != permissions.Length) throw new MiniAppException("INVALID_SCOPE", "Có quyền chưa được Admin duyệt.", 403);
        var granted = await db.MiniAppUserConsents.Where(c => c.AccountId == accountId && c.MiniAppId == app.Id && c.Granted).Select(c => c.Permission.Code).ToArrayAsync(ct);
        var missing = approved.Where(p => !granted.Contains(p.Code)).ToArray();
        if (missing.Length > 0) return new(app.Id, config.Name, config.IconUrl, true, missing, null);
        var raw = credentials.CreateLaunchCode();
        var application = await applications.FindByClientIdAsync(input.ClientId, ct) ?? throw new MiniAppException("INVALID_CLIENT", "Ứng dụng không hoạt động.", 403);
        var descriptor = new OpenIddictTokenDescriptor { ApplicationId = await applications.GetIdAsync(application, ct), ReferenceId = credentials.HashLaunchCode(raw), Subject = accountId.ToString(), Type = "ankt_handoff", Status = Statuses.Valid, CreationDate = clock.GetUtcNow(), ExpirationDate = clock.GetUtcNow().AddSeconds(60) };
        descriptor.Properties["transaction"] = JsonSerializer.SerializeToElement(input with { SessionToken = null });
        descriptor.Properties["consent_version"] = JsonSerializer.SerializeToElement(await ConsentVersion(accountId, app.Id, permissions, ct));
        descriptor.Properties["published_version"] = JsonSerializer.SerializeToElement(app.PublishedVersion);
        await tokens.CreateAsync(descriptor, ct);
        db.MiniAppAuditLogs.Add(new() { MiniAppId = app.Id, ActorAccountId = accountId, Action = "OpenApiHandoffIssued" });
        await db.SaveChangesAsync(ct);
        return new(app.Id, config.Name, config.IconUrl, false, [], raw);
    }

    public async Task<ClaimsPrincipal?> Redeem(string ticket, OpenIddictRequest request, CancellationToken ct)
    {
        var entry = await tokens.FindByReferenceIdAsync(credentials.HashLaunchCode(ticket), ct);
        if (entry is null || await tokens.GetTypeAsync(entry, ct) != "ankt_handoff" || await tokens.GetStatusAsync(entry, ct) != Statuses.Valid || await tokens.GetExpirationDateAsync(entry, ct) <= clock.GetUtcNow()) return null;
        var properties = await tokens.GetPropertiesAsync(entry, ct);
        var transaction = properties["transaction"].Deserialize<OpenApiTransaction>();
        if (transaction is null || transaction.ClientId != request.ClientId || transaction.RedirectUri != request.RedirectUri || transaction.State != request.State || transaction.Nonce != request.Nonce || transaction.CodeChallenge != request.CodeChallenge || transaction.CodeChallengeMethod != request.CodeChallengeMethod || !transaction.Scopes.Order().SequenceEqual(request.GetScopes().Order())) return null;
        var app = await FindApp(transaction.ClientId, ct);
        if (app is null || !properties.TryGetValue("published_version", out var publishedVersion) || publishedVersion.GetInt32() != app.PublishedVersion) return null;
        if (!Guid.TryParse(await tokens.GetSubjectAsync(entry, ct), out var accountId) || !await tokens.TryRedeemAsync(entry, ct)) return null;
        var principal = await CreatePrincipal(accountId, transaction.ClientId, transaction.Scopes, ct);
        if (principal is null || !properties.TryGetValue("consent_version", out var version) || version.GetString() != principal.GetClaim("consent_version")) return null;
        if (transaction.SessionId is Guid session && !await db.MiniAppRuntimeSessions.AnyAsync(s => s.Id == session && s.AccountId == accountId && s.MiniAppId == Guid.Parse(principal.GetClaim("app_id")!) && s.ClosedAt == null && s.ExpiresAt > DateTime.UtcNow, ct)) return null;
        if (transaction.SessionId is Guid runtimeSession)
            principal.SetClaim("runtime_session_id", runtimeSession.ToString()).SetDestinations(c => c.Type is "client_id" or "app_id" or "consent_version" or "published_version" ? [Destinations.AccessToken] : [Destinations.AccessToken, Destinations.IdentityToken]);
        return principal;
    }

    public async Task<ClaimsPrincipal?> CreatePrincipal(Guid accountId, string clientId, IEnumerable<string> scopes, CancellationToken ct)
    {
        var app = await FindApp(clientId, ct); if (app is null) return null;
        var requested = scopes.ToArray(); var config = MiniAppConfigurationSnapshot.Published(app);
        if (config.AuthenticationMode != "AnktSso" || !requested.Contains("openid")) return null;
        string[] permissions; try { permissions = OpenApiPolicy.PermissionsFor(requested); } catch (ArgumentException) { return null; }
        var live = await db.MiniAppUserConsents.Where(c => c.AccountId == accountId && c.MiniAppId == app.Id && c.Granted && c.Permission.Status == MiniAppPermissionStatus.Active).Select(c => c.Permission.Code).ToArrayAsync(ct);
        if (permissions.Any(p => !config.Permissions.Contains(p) || !live.Contains(p))) return null;
        var account = await db.Accounts.Include(a => a.User).SingleOrDefaultAsync(a => a.Id == accountId && a.Status == AccountStatus.Active && a.DeletedAt == null, ct);
        if (account is null) return null;
        var link = await db.MiniAppExternalIdentities.SingleOrDefaultAsync(i => i.AccountId == accountId && i.MiniAppId == app.Id, ct);
        if (link is null)
        {
            link = new() { AccountId = accountId, MiniAppId = app.Id, Subject = credentials.CreateSubject() }; db.Add(link);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                db.Entry(link).State = EntityState.Detached;
                link = await db.MiniAppExternalIdentities.SingleOrDefaultAsync(i => i.AccountId == accountId && i.MiniAppId == app.Id, ct);
                if (link is null) throw;
            }
        }
        var identity = new ClaimsIdentity("OpenApi", Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, link.Subject).SetClaim("client_id", clientId).SetClaim("app_id", app.Id.ToString());
        identity.SetClaim("consent_version", await ConsentVersion(accountId, app.Id, permissions, ct));
        identity.SetClaim("published_version", app.PublishedVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (requested.Contains("profile")) { identity.SetClaim(Claims.Name, account.User?.DisplayName); identity.SetClaim("picture", account.User?.AvatarUrl); }
        if (requested.Contains("email")) identity.SetClaim(Claims.Email, account.Email);
        if (requested.Contains("phone")) identity.SetClaim(Claims.PhoneNumber, account.Phone);
        identity.SetDestinations(c => c.Type is "client_id" or "app_id" or "consent_version" or "published_version" ? [Destinations.AccessToken] : [Destinations.AccessToken, Destinations.IdentityToken]);
        return new ClaimsPrincipal(identity).SetScopes(requested);
    }

    public async Task<OpenApiIdentity?> Validate(ClaimsPrincipal principal, CancellationToken ct, bool requireActiveRuntime = false)
    {
        var clientId = principal.GetClaim("client_id"); var subject = principal.GetClaim(Claims.Subject);
        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(subject)) return null;
        var app = await FindApp(clientId, ct); if (app is null) return null;
        var link = await db.MiniAppExternalIdentities.Include(i => i.Account).ThenInclude(a => a.User).SingleOrDefaultAsync(i => i.MiniAppId == app.Id && i.Subject == subject, ct);
        if (link is null || link.Account.Status != AccountStatus.Active || link.Account.DeletedAt != null) return null;
        var scopes = principal.GetScopes().ToArray();
        var current = await CreatePrincipal(link.AccountId, clientId, scopes, ct);
        if (current is null || principal.GetClaim("consent_version") != current.GetClaim("consent_version") || principal.GetClaim("published_version") != current.GetClaim("published_version")) return null;
        if (requireActiveRuntime && principal.GetClaim("runtime_session_id") is string runtimeId &&
            (!Guid.TryParse(runtimeId, out var runtimeSession) || !await db.MiniAppRuntimeSessions.AnyAsync(s => s.Id == runtimeSession && s.MiniAppId == app.Id && s.AccountId == link.AccountId && s.PublishedVersion == app.PublishedVersion && s.ClosedAt == null && s.ExpiresAt > DateTime.UtcNow, ct))) return null;
        return new(app, link.Account, link, scopes);
    }

    private async Task<string> ConsentVersion(Guid accountId, Guid appId, string[] permissions, CancellationToken ct)
    {
        var grants = await db.MiniAppUserConsents.AsNoTracking().Where(c => c.AccountId == accountId && c.MiniAppId == appId && permissions.Contains(c.Permission.Code))
            .Select(c => new { c.Id, c.GrantedAt, c.Granted, c.RevokedAt }).ToListAsync(ct);
        var value = string.Join("|", grants.OrderBy(c => c.Id).Select(c => $"{c.Id}:{c.Granted}:{c.GrantedAt:O}:{c.RevokedAt:O}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    public async Task RevokeClient(Guid appId, CancellationToken ct)
    {
        await foreach (var token in tokens.FindByApplicationIdAsync(appId.ToString(), ct)) await tokens.TryRevokeAsync(token, ct);
        db.MiniAppAuditLogs.Add(new() { MiniAppId = appId, Action = "OpenApiTokensRevoked" }); await db.SaveChangesAsync(ct);
    }
}
