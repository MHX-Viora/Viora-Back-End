using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.Sso;

namespace Viora.Infrastructure.Developers;

public sealed class SandboxTestService(AppDbContext db, IOptions<SandboxTestOptions> configured, TimeProvider clock)
{
    public const string SessionClaim = "ankt_sandbox_test_session";
    public bool Enabled => Options.Enabled;
    private SandboxTestOptions Options => configured.Value;
    private static readonly SemaphoreSlim TemplateGate = new(1, 1);
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Credential() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    private static bool ValidCredential(string? value) => value is { Length: 43 } && value.All(x => char.IsAsciiLetterOrDigit(x) || x is '-' or '_');
    private static DeveloperAuthException Denied() => new(403, "SANDBOX_TEST_UNAVAILABLE", "Phiên kiểm thử không còn hợp lệ hoặc không thuộc ứng dụng này.");

    public async Task<MiniApp> OwnerAsync(Guid owner, Guid sourceSession, string appId, CancellationToken ct)
    {
        var now = Now;
        if (!await db.DeveloperSessions.AnyAsync(x => x.Id == sourceSession && x.DeveloperAccountId == owner && x.RevokedAt == null &&
            x.ExpiresAt > now && x.AbsoluteExpiresAt > now && x.Account.Status == DeveloperStatus.Active &&
            (x.Account.LockedUntil == null || x.Account.LockedUntil <= now), ct)) throw Denied();
        return await db.MiniApps.AsNoTracking().SingleOrDefaultAsync(x => x.AppId == appId && x.DeveloperAccountId == owner &&
            x.DeletedAt == null && x.Status != MiniAppStatus.Suspended && x.Status != MiniAppStatus.Rejected, ct)
            ?? throw new DeveloperAuthException(404, "SANDBOX_TEST_NOT_FOUND", "Không tìm thấy Mini App được phép kiểm thử.");
    }

    private async Task<SandboxIdentityTemplate> TemplateAsync(CancellationToken ct)
    {
        await TemplateGate.WaitAsync(ct);
        try { return await LoadTemplateAsync(ct); }
        finally { TemplateGate.Release(); }
    }

    private async Task<SandboxIdentityTemplate> LoadTemplateAsync(CancellationToken ct)
    {
        if (!Enabled || !Options.SsoEnabled) throw Denied();
        var template = await db.SandboxIdentityTemplates.IgnoreQueryFilters().Include(x => x.Account).ThenInclude(x => x.User)
            .SingleOrDefaultAsync(x => x.Id == SandboxIdentityTemplate.DefaultId, ct);
        if (template != null)
        {
            if (!template.Account.IsSandbox || template.Account.User == null || template.Account.Role != AccountRole.User ||
                template.Account.Status != AccountStatus.Active || template.Account.DeletedAt != null) throw Denied();
            var changed = false;
            if (template.Account.User.AvatarUrl == null && SandboxTestOptions.CloudinaryUrl(Options.AvatarUrl))
            { template.Account.User.AvatarUrl = Options.AvatarUrl; changed = true; }
            if (template.Account.User.CoverUrl == null && SandboxTestOptions.CloudinaryUrl(Options.CoverUrl))
            { template.Account.User.CoverUrl = Options.CoverUrl; changed = true; }
            if (changed) await db.SaveChangesAsync(ct);
            return template;
        }
        var account = await db.Accounts.IgnoreQueryFilters().Include(x => x.User)
            .SingleOrDefaultAsync(x => x.Email == "sandbox-test@domain.test", ct);
        // A reserved email alone is not proof that this is a test identity.
        if (account != null && (!account.IsSandbox || account.User == null || account.Role != AccountRole.User ||
            account.Status != AccountStatus.Active || account.DeletedAt != null))
            throw new DeveloperAuthException(409, "SANDBOX_TEMPLATE_CONFLICT", "Email mẫu đã được sử dụng; không thay đổi tài khoản hiện có.");
        account ??= new Account { Email = "sandbox-test@domain.test", IsSandbox = true, Status = AccountStatus.Active };
        account.User ??= new User { DisplayName = "ANKT Sandbox User", Gender = Gender.Male, IsVerified = true,
            AvatarUrl = SandboxTestOptions.CloudinaryUrl(Options.AvatarUrl) ? Options.AvatarUrl : null,
            CoverUrl = SandboxTestOptions.CloudinaryUrl(Options.CoverUrl) ? Options.CoverUrl : null };
        if (account.User.AvatarUrl == null && SandboxTestOptions.CloudinaryUrl(Options.AvatarUrl)) account.User.AvatarUrl = Options.AvatarUrl;
        if (account.User.CoverUrl == null && SandboxTestOptions.CloudinaryUrl(Options.CoverUrl)) account.User.CoverUrl = Options.CoverUrl;
        template = new SandboxIdentityTemplate { Id = SandboxIdentityTemplate.DefaultId, Account = account };
        db.SandboxIdentityTemplates.Add(template); await db.SaveChangesAsync(ct); return template;
    }

    private static SandboxTestProfile Profile(SandboxIdentityTemplate template) => new(template.FullName,
        template.Account.User!.DisplayName, template.Nickname, template.Account.Email!, template.Account.User.Gender.ToString(),
        template.Birthdate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        template.Account.User.AvatarUrl, template.Account.User.CoverUrl, false, "SandboxTest",
        template.Account.User.Id, template.Nickname, true, template.Account.User.IsVerified, template.Account.Status == AccountStatus.Active);

    public async Task<SandboxTestOverview> OverviewAsync(Guid owner, Guid source, string appId, CancellationToken ct)
    {
        if (!Enabled || !Options.SsoEnabled) throw Denied();
        var app = await OwnerAsync(owner, source, appId, ct);
        var template = await TemplateAsync(ct);
        var client = await db.MiniAppOAuthClients.AsNoTracking().SingleOrDefaultAsync(x => x.MiniAppId == app.Id && x.Environment == MiniAppOAuthEnvironment.Sandbox, ct);
        string? error = !Options.Enabled ? "SANDBOX_TEST_DISABLED" : !Options.SsoEnabled ? "SSO_RUNTIME_DISABLED" : client == null ? "SSO_CLIENT_UNAVAILABLE" : null;
        if (error == null) try { await new SsoClientReader(db, clock).EligibleAsync(client!.ClientKey, ["openid"], "Sandbox", ct); }
        catch (DeveloperAuthException e) { error = e.Code; }
        if (error == null && (!SandboxTestOptions.CloudinaryUrl(template.Account.User?.AvatarUrl) || !SandboxTestOptions.CloudinaryUrl(template.Account.User?.CoverUrl)))
            error = "SANDBOX_ASSETS_MISSING";
        var redirects = await db.MiniAppRedirectUris.AsNoTracking().Where(x => x.MiniAppId == app.Id && x.Environment == MiniAppOAuthEnvironment.Sandbox).Select(x => x.RedirectUri).ToArrayAsync(ct);
        if (error == null && redirects.Length == 0) error = "SSO_REDIRECT_REQUIRED";
        var sessions = await db.SandboxIdentitySessions.AsNoTracking().Where(x => x.Identity.MiniAppId == app.Id && x.Identity.DeveloperAccountId == owner)
            .OrderByDescending(x => x.CreatedAt).Take(10).Select(x => new SandboxTestSessionDto(x.Id, x.ExpiresAt, x.RevokedAt, x.AuthorizedAt, x.TokenIssuedAt, x.UserInfoAt)).ToArrayAsync(ct);
        var launchEndpoint = Uri.TryCreate(Options.Issuer, UriKind.Absolute, out var issuer) && issuer.Scheme == "https"
            ? new Uri(issuer, "/sso/sandbox-test/start").AbsoluteUri : null;
        return new(Profile(template), error == null, error, client?.ClientKey, redirects, launchEndpoint, sessions);
    }

    public async Task<SandboxTestLaunch> CreateAsync(Guid owner, Guid source, string appId, CancellationToken ct)
    {
        var overview = await OverviewAsync(owner, source, appId, ct);
        if (!overview.Ready) throw new DeveloperAuthException(409, overview.ErrorCode!, "Cấu hình Sandbox Test chưa sẵn sàng. Kiểm tra trạng thái SSO và ảnh mẫu.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var app = await OwnerAsync(owner, source, appId, ct);
        var client = await new SsoClientReader(db, clock).EligibleAsync(overview.ClientId, ["openid"], "Sandbox", ct);
        var identity = await db.SandboxTestIdentities.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.DeveloperAccountId == owner && x.MiniAppId == app.Id, ct);
        if (identity == null)
        {
            var templateAccount = await db.SandboxIdentityTemplates.Select(x => x.AccountId).SingleAsync(ct);
            identity = new SandboxTestIdentity { DeveloperAccountId = owner, MiniAppId = app.Id, AccountId = templateAccount };
            db.SandboxTestIdentities.Add(identity); await db.SaveChangesAsync(ct);
        }
        var now = Now;
        if (await db.SandboxIdentitySessions.CountAsync(x => x.DeveloperSessionId == source && x.RevokedAt == null && x.ExpiresAt > now, ct) >= 3)
            throw new DeveloperAuthException(429, "SANDBOX_TEST_QUOTA", "Dừng phiên cũ trước khi mở thêm phiên kiểm thử.");
        var ticket = Credential();
        var session = new SandboxIdentitySession { IdentityId = identity.Id, DeveloperSessionId = source, OAuthClientId = client.Id,
            ClientVersion = client.Version, LaunchHash = Hash(ticket), LaunchExpiresAt = now.AddSeconds(60), ExpiresAt = now.AddMinutes(Options.SessionMinutes) };
        db.SandboxIdentitySessions.Add(session); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return new(session.Id, new Uri(new Uri(Options.Issuer), "/sso/sandbox-test/start").AbsoluteUri, ticket, session.ExpiresAt);
    }

    public async Task<(string Credential, DateTime ExpiresAt, string Website, string ClientId)> ConsumeAsync(string? ticket, CancellationToken ct)
    {
        if (!Options.Enabled || !Options.SsoEnabled || !ValidCredential(ticket)) throw Denied();
        var hash = Hash(ticket!); var now = Now;
        var session = await db.SandboxIdentitySessions.AsNoTracking().SingleOrDefaultAsync(x => x.LaunchHash == hash && x.LaunchConsumedAt == null && x.LaunchExpiresAt > now, ct) ?? throw Denied();
        var valid = await RequireAsync(session.Id, null, ct);
        var credential = Credential();
        if (await db.SandboxIdentitySessions.Where(x => x.Id == session.Id && x.LaunchConsumedAt == null && x.RevokedAt == null && x.LaunchExpiresAt > now)
            .ExecuteUpdateAsync(x => x.SetProperty(s => s.LaunchConsumedAt, now).SetProperty(s => s.BrowserHash, Hash(credential)), ct) != 1) throw Denied();
        var website = await db.SandboxTestIdentities.Where(x => x.Id == session.IdentityId).Select(x => x.MiniApp.WebsiteUrl).SingleAsync(ct);
        return (credential, session.ExpiresAt, website!, valid.OAuthClient.ClientKey);
    }

    public async Task<SandboxIdentitySession> RequireAsync(Guid id, string? clientId, CancellationToken ct)
    {
        if (!Options.Enabled || !Options.SsoEnabled) throw Denied();
        var now = Now;
        var session = await db.SandboxIdentitySessions.IgnoreQueryFilters().AsNoTracking().Include(x => x.Identity).ThenInclude(x => x.Account)
            .Include(x => x.OAuthClient).SingleOrDefaultAsync(x => x.Id == id && x.ExpiresAt > now && x.RevokedAt == null, ct) ?? throw Denied();
        if (session.Identity.Account is not { IsSandbox: true, Role: AccountRole.User, Status: AccountStatus.Active, DeletedAt: null } ||
            session.OAuthClient.Environment != MiniAppOAuthEnvironment.Sandbox || session.OAuthClient.Version != session.ClientVersion ||
            session.OAuthClient.MiniAppId != session.Identity.MiniAppId || clientId != null && session.OAuthClient.ClientKey != clientId) throw Denied();
        if (!await db.SandboxIdentityTemplates.AnyAsync(x => x.Id == SandboxIdentityTemplate.DefaultId && x.AccountId == session.Identity.AccountId, ct)) throw Denied();
        var appId = await db.MiniApps.Where(x => x.Id == session.Identity.MiniAppId).Select(x => x.AppId).SingleAsync(ct);
        await OwnerAsync(session.Identity.DeveloperAccountId, session.DeveloperSessionId, appId, ct);
        await new SsoClientReader(db, clock).EligibleAsync(session.OAuthClient.ClientKey, ["openid"], "Sandbox", ct);
        return session;
    }

    public async Task<SandboxIdentitySession?> AuthenticateAsync(string? credential, string clientId, CancellationToken ct)
    {
        if (!ValidCredential(credential)) return null;
        var hash = Hash(credential!);
        var id = await db.SandboxIdentitySessions.Where(x => x.BrowserHash == hash && x.LaunchConsumedAt != null).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        if (id == null) return null;
        try { return await RequireAsync(id.Value, clientId, ct); } catch (DeveloperAuthException) { return null; }
    }

    public async Task<SandboxTestProfile> ProfileForAccountAsync(Guid account, Guid app, CancellationToken ct)
    {
        if (!await db.SandboxTestIdentities.AnyAsync(x => x.AccountId == account && x.MiniAppId == app && x.MiniApp.DeveloperAccountId == x.DeveloperAccountId, ct)) throw Denied();
        var template = await TemplateAsync(ct);
        if (template.AccountId != account) throw Denied();
        return Profile(template);
    }

    public async Task RevokeAsync(Guid owner, Guid source, string appId, Guid sessionId, CancellationToken ct)
    {
        var app = await OwnerAsync(owner, source, appId, ct);
        if (await db.SandboxIdentitySessions.Where(x => x.Id == sessionId && x.Identity.DeveloperAccountId == owner && x.Identity.MiniAppId == app.Id)
            .ExecuteUpdateAsync(x => x.SetProperty(s => s.RevokedAt, Now), ct) != 1) throw Denied();
    }

    public async Task RecordAsync(System.Security.Claims.ClaimsPrincipal principal, string stage, CancellationToken ct)
    {
        if (!Guid.TryParse(principal.FindFirst(SessionClaim)?.Value, out var id)) return;
        var query = db.SandboxIdentitySessions.Where(x => x.Id == id);
        if (stage == "authorize") await query.ExecuteUpdateAsync(x => x.SetProperty(s => s.AuthorizedAt, Now), ct);
        if (stage == "token") await query.ExecuteUpdateAsync(x => x.SetProperty(s => s.TokenIssuedAt, Now), ct);
        if (stage == "userinfo") await query.ExecuteUpdateAsync(x => x.SetProperty(s => s.UserInfoAt, Now), ct);
    }
}
