using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Developers;

public sealed class MiniAppService(AppDbContext db, MiniAppSecretProtector protector, IOptions<MiniAppOptions> options, IHostEnvironment environment,
    Viora.Infrastructure.Sso.MiniAppOidcRegistry? oidcRegistry = null, MiniAppVersionService? versions = null)
{
    private static string RandomId(string prefix) => prefix + WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    private static DeveloperAuthException Missing() => new(404, "MINI_APP_NOT_FOUND", "Không tìm thấy Mini App.");
    private static MiniAppDto Map(MiniApp a) => new(a.AppId, a.Name, a.Category, a.LogoUrl, a.ShortDescription, a.Description,
        a.WebsiteUrl, a.SupportEmail, a.PrivacyPolicyUrl, a.TermsOfServiceUrl, a.Status.ToString(), a.CreatedAt, a.UpdatedAt);
    private async Task<MiniApp> Own(Guid owner, string appId, CancellationToken ct) => await db.MiniApps
        .SingleOrDefaultAsync(a => a.AppId == appId && a.DeveloperAccountId == owner && a.DeletedAt == null, ct) ?? throw Missing();
    private void Audit(MiniApp app, Guid owner, string action) => db.MiniAppAuditLogs.Add(new() { MiniAppId = app.Id, ActorDeveloperId = owner, Action = action });
    private void Touch(MiniApp app) { app.Version = Guid.NewGuid(); db.Entry(app).Property(x => x.UpdatedAt).IsModified = true; }

    public async Task<object> List(Guid owner, int page, CancellationToken ct)
    {
        page = Math.Clamp(page, 1, 100000);
        var query = db.MiniApps.AsNoTracking().Where(a => a.DeveloperAccountId == owner && a.DeletedAt == null);
        var total = await query.CountAsync(ct);
        var apps = await query.OrderByDescending(a => a.UpdatedAt).ThenBy(a => a.Id).Skip((page - 1) * 20).Take(20).ToListAsync(ct);
        return new { items = apps.Select(Map), page, total, pageSize = 20 };
    }
    public async Task<MiniAppDto> Get(Guid owner, string appId, CancellationToken ct) => Map(await Own(owner, appId, ct));

    public async Task<MiniAppDto> Create(Guid owner, MiniAppCreateRequest r, CancellationToken ct)
    {
        MiniAppInput.ValidateCreate(r);
        var name = r.Name.Trim();
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { name, r.Category, r.LogoUploadId }))));
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var existing = await db.MiniApps.SingleOrDefaultAsync(a => a.DeveloperAccountId == owner && a.CreateRequestId == r.RequestId, ct);
        if (existing is not null)
        {
            if (existing.CreateFingerprint != fingerprint || existing.DeletedAt is not null) throw new DeveloperAuthException(409, "REQUEST_CONFLICT", "Mã tạo ứng dụng đã được sử dụng. Hãy mở lại biểu mẫu.");
            return Map(existing);
        }
        if (await db.MiniApps.CountAsync(a => a.DeveloperAccountId == owner && a.DeletedAt == null, ct) >= Math.Clamp(options.Value.MaxAppsPerDeveloper, 1, 500))
            throw new DeveloperAuthException(409, "APP_LIMIT_REACHED", "Bạn đã đạt giới hạn Mini App.");
        var app = new MiniApp { AppId = RandomId("app_"), DeveloperAccountId = owner, Name = name, Category = r.Category,
            Status = MiniAppStatus.ConfigurationIncomplete, CreateRequestId = r.RequestId, CreateFingerprint = fingerprint };
        db.MiniApps.Add(app);
        await AttachLogo(app, owner, r.LogoUploadId, ct);
        var client = new MiniAppOAuthClient { MiniAppId = app.Id, ClientKey = RandomId("ck_") };
        db.MiniAppOAuthClients.Add(client); AddSecret(client);
        Audit(app, owner, "mini_app.created");
        await db.SaveChangesAsync(ct);
        if (oidcRegistry != null) await oidcRegistry.SynchronizeAsync(app.Id, ct);
        await tx.CommitAsync(ct);
        return Map(app);
    }
    private MiniAppClientSecret AddSecret(MiniAppOAuthClient client)
    {
        var encrypted = protector.Encrypt(RandomId("cs_"), client.Id);
        var secret = new MiniAppClientSecret { OAuthClientId = client.Id, SecretEncrypted = encrypted.Ciphertext, SecretKeyVersion = encrypted.Version };
        db.MiniAppClientSecrets.Add(secret); return secret;
    }
    private async Task AttachLogo(MiniApp app, Guid owner, Guid? uploadId, CancellationToken ct)
    {
        if (uploadId is null) return;
        var upload = await db.MiniAppLogoUploads.SingleOrDefaultAsync(x => x.Id == uploadId && x.DeveloperAccountId == owner && x.MiniAppId == null && x.ExpiresAt > DateTime.UtcNow && x.Url != null, ct);
        if (upload is null) throw MiniAppInput.Invalid("Logo đã hết hạn hoặc không thuộc tài khoản của bạn. Hãy tải lại.");
        // Conditional claim prevents two apps from attaching the same temporary upload.
        var claimedAt = DateTime.UtcNow;
        var claimed = await db.MiniAppLogoUploads.Where(x => x.Id == upload.Id && x.MiniAppId == null && x.ExpiresAt > claimedAt)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, claimedAt.AddMinutes(30)), ct);
        if (claimed != 1) throw new DeveloperAuthException(409, "LOGO_CONFLICT", "Logo vừa được sử dụng. Hãy tải lại.");
        upload.MiniAppId = app.Id; app.LogoUrl = upload.Url;
        // Previously attached logos become eligible for cleanup after replacement.
        await db.MiniAppLogoUploads.Where(x => x.MiniAppId == app.Id && x.Id != upload.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.MiniAppId, (Guid?)null).SetProperty(x => x.ExpiresAt, DateTime.UtcNow.AddHours(1)), ct);
    }
    public async Task<MiniAppDto> Update(Guid owner, string appId, MiniAppUpdateRequest r, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var app = await Own(owner, appId, ct);
        var previousWebsite = app.WebsiteUrl;
        if (r.Name is not null) app.Name = MiniAppInput.Text(r.Name, 120, true);
        if (r.Category is not null) { if (!MiniAppInput.Categories.Contains(r.Category)) throw MiniAppInput.Invalid("Danh mục không hợp lệ."); app.Category = r.Category; }
        if (r.ShortDescription is not null) app.ShortDescription = MiniAppInput.Text(r.ShortDescription, 300);
        if (r.Description is not null) app.Description = MiniAppInput.Text(r.Description, 10000);
        if (r.WebsiteUrl is not null) app.WebsiteUrl = PublicWebAddress.Normalize(r.WebsiteUrl, allowEmpty: true);
        if (r.PrivacyPolicyUrl is not null) app.PrivacyPolicyUrl = PublicWebAddress.Normalize(r.PrivacyPolicyUrl, allowEmpty: true);
        if (r.TermsOfServiceUrl is not null) app.TermsOfServiceUrl = PublicWebAddress.Normalize(r.TermsOfServiceUrl, allowEmpty: true);
        if (r.SupportEmail is not null) app.SupportEmail = string.IsNullOrWhiteSpace(r.SupportEmail) ? "" : DeveloperInput.Email(r.SupportEmail).ToLowerInvariant();
        await AttachLogo(app, owner, r.LogoUploadId, ct);
        await MiniAppDomainLifecycle.ChangeWebsite(db, app, previousWebsite, protector, ct);
        // Only draft-era states may be computed by this feature.
        if (app.Status is MiniAppStatus.Draft or MiniAppStatus.ConfigurationIncomplete)
            app.Status = !string.IsNullOrEmpty(app.LogoUrl) && !string.IsNullOrEmpty(app.ShortDescription) && !string.IsNullOrEmpty(app.WebsiteUrl)
                && !string.IsNullOrEmpty(app.SupportEmail) && !string.IsNullOrEmpty(app.PrivacyPolicyUrl) && !string.IsNullOrEmpty(app.TermsOfServiceUrl)
                ? MiniAppStatus.Draft : MiniAppStatus.ConfigurationIncomplete;
        Touch(app); Audit(app, owner, "mini_app.updated");
        await db.SaveChangesAsync(ct);
        if (versions != null) await versions.SyncWorkingAsync(app, ct);
        if (oidcRegistry != null) await oidcRegistry.SynchronizeAsync(app.Id, ct);
        await tx.CommitAsync(ct); return Map(app);
    }
    public async Task Delete(Guid owner, string appId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var app = await Own(owner, appId, ct); app.DeletedAt = DateTime.UtcNow; Touch(app);
        await MiniAppDomainLifecycle.Revoke(db, app.Id, ct);
        var clients = db.MiniAppOAuthClients.Where(x => x.MiniAppId == app.Id);
        await clients.ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTime.UtcNow), ct);
        await db.MiniAppClientSecrets.Where(x => clients.Select(c => c.Id).Contains(x.OAuthClientId) && x.RevokedAt == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTime.UtcNow), ct);
        await db.MiniAppLogoUploads.Where(x => x.MiniAppId == app.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.MiniAppId, (Guid?)null).SetProperty(x => x.ExpiresAt, DateTime.UtcNow.AddHours(1)), ct);
        Audit(app, owner, "mini_app.deleted"); await db.SaveChangesAsync(ct);
        if (oidcRegistry != null) await oidcRegistry.SynchronizeAsync(app.Id, ct);
        await tx.CommitAsync(ct);
    }
    public async Task<MiniAppCredentialsDto> Credentials(Guid owner, string appId, bool rotate, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var app = await Own(owner, appId, ct);
        var client = await db.MiniAppOAuthClients.SingleAsync(x => x.MiniAppId == app.Id && x.Environment == MiniAppOAuthEnvironment.Sandbox && x.RevokedAt == null, ct);
        if (client.ClientType == "Public") throw new DeveloperAuthException(409, "PUBLIC_CLIENT_NO_SECRET", "Public Client chỉ sử dụng PKCE và không có Client Secret.");
        MiniAppClientSecret secret;
        if (rotate)
        {
            Touch(app);
            client.Version = Guid.NewGuid();
            // Persist revocation before inserting the new row to honor the active-secret unique index.
            await db.MiniAppClientSecrets.Where(x => x.OAuthClientId == client.Id && x.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTime.UtcNow), ct);
            secret = AddSecret(client); Audit(app, owner, "credentials.rotated");
        }
        else
        {
            secret = await db.MiniAppClientSecrets.SingleAsync(x => x.OAuthClientId == client.Id && x.RevokedAt == null, ct);
            if (secret.ExpiresAt <= DateTime.UtcNow) throw new DeveloperAuthException(409, "SECRET_EXPIRED", "Secret đã hết hạn. Hãy cấp lại.");
            Audit(app, owner, "credentials.viewed");
        }
        var plaintext = protector.Decrypt(secret.SecretEncrypted, secret.SecretKeyVersion, client.Id);
        await db.SaveChangesAsync(ct);
        if (rotate && oidcRegistry != null) await oidcRegistry.SynchronizeAsync(app.Id, ct);
        await tx.CommitAsync(ct);
        return new(app.AppId, client.ClientKey, plaintext, client.ClientType, secret.CreatedAt);
    }
    public async Task<string[]> Redirects(Guid owner, string appId, CancellationToken ct)
    {
        var app = await Own(owner, appId, ct);
        return await db.MiniAppRedirectUris.Where(x => x.MiniAppId == app.Id && x.Environment == MiniAppOAuthEnvironment.Sandbox).OrderBy(x => x.RedirectUri).Select(x => x.RedirectUri).ToArrayAsync(ct);
    }
    public async Task<string[]> SaveRedirects(Guid owner, string appId, MiniAppRedirectRequest r, CancellationToken ct)
    {
        if (r.RedirectUris is null || r.RedirectUris.Length > 20) throw MiniAppInput.Invalid("Tối đa 20 callback URI.");
        var urls = r.RedirectUris.Select(x => MiniAppInput.Url(x, environment.IsDevelopment(), true)).Distinct(StringComparer.Ordinal).ToArray();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var app = await Own(owner, appId, ct); Touch(app);
        await db.MiniAppRedirectUris.Where(x => x.MiniAppId == app.Id && x.Environment == MiniAppOAuthEnvironment.Sandbox).ExecuteDeleteAsync(ct);
        db.MiniAppRedirectUris.AddRange(urls.Select(url => new MiniAppRedirectUri { MiniAppId = app.Id, RedirectUri = url }));
        Audit(app, owner, "callbacks.updated"); await db.SaveChangesAsync(ct);
        if (versions != null) await versions.SyncWorkingAsync(app, ct);
        if (oidcRegistry != null) await oidcRegistry.SynchronizeAsync(app.Id, ct);
        await tx.CommitAsync(ct); return urls;
    }
    public async Task<MiniAppActivityDto[]> Activity(Guid owner, string appId, CancellationToken ct)
    {
        var app = await Own(owner, appId, ct);
        return await db.MiniAppAuditLogs.AsNoTracking().Where(x => x.MiniAppId == app.Id).OrderByDescending(x => x.CreatedAt)
            .Take(100).Select(x => new MiniAppActivityDto(x.Action, x.CreatedAt)).ToArrayAsync(ct);
    }
}
