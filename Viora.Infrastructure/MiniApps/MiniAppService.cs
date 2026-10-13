using Microsoft.EntityFrameworkCore;
using Viora.Application.MiniApps;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.MiniApps;

public sealed partial class MiniAppService(AppDbContext db, IClientCredentialService credentials) : IMiniAppService
{
    public async Task<MiniAppListResponse> GetActiveAsync(CancellationToken cancellationToken)
    {
        var apps = await ActiveAppQuery().Where(app => !app.HiddenFromCatalog && (app.CatalogCategoryId == null || db.MiniAppCategories.Any(category => category.Id == app.CatalogCategoryId && category.IsActive))).AsNoTracking().OrderBy(app => app.CatalogName).Take(60).ToListAsync(cancellationToken);
        var categories = await db.MiniAppCategories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
        var items = apps.Select(app => { var c = MiniAppConfigurationSnapshot.Published(app); return new MiniAppListItemDto(app.Id, c.Name, c.Slug, c.Description, c.IconUrl, c.CoverUrl, c.IsFeatured, app.Developer.Name, c.CategoryId, c.CategoryId is Guid categoryId ? categories.GetValueOrDefault(categoryId) : null, c.AuthenticationMode, app.PublishedVersion, c.AuthenticationMode == "AnktSso" && c.AutoLogin); }).ToArray();
        return new(items);
    }

    public async Task<MiniAppDetailDto?> GetActiveDetailAsync(Guid id, CancellationToken cancellationToken)
    {
        var app = await ActiveAppQuery().AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (app is null) return null;
        var c = MiniAppConfigurationSnapshot.Published(app);
        var category = c.CategoryId is Guid categoryId ? await db.MiniAppCategories.Where(x => x.Id == categoryId).Select(x => x.Name).SingleOrDefaultAsync(cancellationToken) : null;
        var verifiedHosts = await db.MiniAppVerifiedDomains.Where(d => d.MiniAppId == id && d.VerifiedAt != null).Select(d => d.Host).ToListAsync(cancellationToken);
        var bridgeOrigins = (c.AllowedOrigins ?? []).Where(o => verifiedHosts.Contains(new Uri(o).IdnHost)).ToArray();
        return new(app.Id, c.Name, c.Slug, c.Description, c.IconUrl, c.CoverUrl, app.Developer.Name, await ApprovedPermissions(app, cancellationToken), app.Status.ToString(), c.WebUrl, c.AllowedDomains, bridgeOrigins, c.CallbackUrls, c.CategoryId, category, c.AuthenticationMode, app.PublishedVersion, app.ClientId, c.AuthenticationMode == "AnktSso" && c.AutoLogin, c.Screenshots, c.PrivacyPolicyUrl, c.TermsUrl, c.SupportEmail, app.PublishedAt, c.SsoMode);
    }

    public async Task<LaunchMiniAppResponse> LaunchAsync(Guid accountId, Guid miniAppId, CancellationToken cancellationToken)
    {
        var app = await AppQuery().SingleOrDefaultAsync(item => item.Id == miniAppId && item.DeletedAt == null, cancellationToken);
        if (app is null) throw new MiniAppException(MiniAppErrorCodes.NotFound, "Mini App không tồn tại.", 404);
        if (app.Developer.Status != DeveloperStatus.Active)
            return await FailLaunch(accountId, app.Id, MiniAppErrorCodes.DeveloperSuspended, "Developer hiện đang bị tạm ngừng.", cancellationToken);
        if (app.Status == MiniAppStatus.Suspended)
            return await FailLaunch(accountId, app.Id, MiniAppErrorCodes.Suspended, "Mini App hiện đang tạm ngừng hoạt động.", cancellationToken);
        if (app.Status != MiniAppStatus.Active || app.PublishedVersion < 1 || app.PublishedConfigurationJson == null)
            return await FailLaunch(accountId, app.Id, MiniAppErrorCodes.NotActive, "Mini App chưa hoạt động.", cancellationToken);

        if (!await db.Accounts.AnyAsync(a => a.Id == accountId && a.Status == AccountStatus.Active && a.DeletedAt == null, cancellationToken)) throw new MiniAppException("ACCOUNT_NOT_ACTIVE", "Active account required.", 403);
        var configuration = MiniAppConfigurationSnapshot.Published(app);
        var domains = MiniAppSecurityPolicy.NormalizeDomains(configuration.AllowedDomains);
        if (!MiniAppSecurityPolicy.IsAllowedHttpsUrl(configuration.WebUrl, domains))
            return await FailLaunch(accountId, app.Id, MiniAppErrorCodes.DomainNotAllowed, "Invalid registered domain.", cancellationToken);
        var token = credentials.CreateLaunchCode();
        var session = new MiniAppRuntimeSession { MiniAppId = app.Id, AccountId = accountId, PublishedVersion = app.PublishedVersion, TokenHash = credentials.HashLaunchCode(token), ExpiresAt = DateTime.UtcNow.AddMinutes(30) };
        db.MiniAppRuntimeSessions.Add(session);
        db.MiniAppLaunchLogs.Add(new MiniAppLaunchLog { AccountId = accountId, MiniAppId = app.Id, Status = MiniAppLaunchStatus.Succeeded });
        await db.SaveChangesAsync(cancellationToken);
        var granted = await GrantedPermissions(accountId, app, cancellationToken);
        var permissions = (await ApprovedPermissions(app, cancellationToken)).Where(p => granted.Contains(p.Code)).ToArray();
        return new(false, permissions, configuration.WebUrl, 1800, domains, session.Id, token);
    }

    public async Task GrantConsentAsync(Guid accountId, Guid miniAppId, GrantConsentRequest request, CancellationToken cancellationToken)
    {
        var requestedCodes = request.Permissions.Where(code => !string.IsNullOrWhiteSpace(code)).Distinct(StringComparer.Ordinal).ToArray();
        if (requestedCodes.Length == 0) throw new MiniAppException("VALIDATION_ERROR", "Cần chọn ít nhất một permission.", 422);
        var appAvailable = await db.MiniApps.AnyAsync(app => app.Id == miniAppId && app.DeletedAt == null && app.Status == MiniAppStatus.Active && app.Developer.Status == DeveloperStatus.Active, cancellationToken);
        if (!appAvailable) throw new MiniAppException(MiniAppErrorCodes.NotActive, "Mini App chưa hoạt động.", 403);
        var app = await AppQuery().AsNoTracking().SingleAsync(a => a.Id == miniAppId, cancellationToken);
        var approvedCodes = MiniAppConfigurationSnapshot.Published(app).Permissions;
        var approved = await db.MiniAppPermissions.Where(p => requestedCodes.Contains(p.Code) && approvedCodes.Contains(p.Code) && MiniAppSecurityPolicy.SupportedPermissionCodes.Contains(p.Code) && p.Status == MiniAppPermissionStatus.Active)
            .Select(p => new { PermissionId = p.Id, p.Code }).ToListAsync(cancellationToken);
        if (approved.Count != requestedCodes.Length)
            throw new MiniAppException(MiniAppErrorCodes.PermissionDenied, "Có permission chưa được Admin duyệt.", 403);

        var existing = await db.MiniAppUserConsents
            .Where(consent => consent.AccountId == accountId && consent.MiniAppId == miniAppId && approved.Select(item => item.PermissionId).Contains(consent.PermissionId))
            .ToDictionaryAsync(consent => consent.PermissionId, cancellationToken);
        var now = DateTime.UtcNow;
        foreach (var permission in approved)
        {
            if (!existing.TryGetValue(permission.PermissionId, out var consent))
            {
                consent = new MiniAppUserConsent { AccountId = accountId, MiniAppId = miniAppId, PermissionId = permission.PermissionId };
                db.MiniAppUserConsents.Add(consent);
            }
            consent.Granted = request.Granted;
            consent.GrantedAt = request.Granted ? now : null;
            consent.RevokedAt = request.Granted ? null : now;
        }
        db.MiniAppAuditLogs.Add(new MiniAppAuditLog { MiniAppId = miniAppId, ActorAccountId = accountId, Action = request.Granted ? "ConsentGranted" : "ConsentRevoked" });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeConsentAsync(Guid accountId, Guid miniAppId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        await db.MiniAppUserConsents
            .Where(consent => consent.AccountId == accountId && consent.MiniAppId == miniAppId && consent.Granted)
            .ExecuteUpdateAsync(setters => setters.SetProperty(consent => consent.Granted, false).SetProperty(consent => consent.RevokedAt, now), cancellationToken);
        db.MiniAppAuditLogs.Add(new MiniAppAuditLog { MiniAppId = miniAppId, ActorAccountId = accountId, Action = "ConsentRevoked" });
        await db.SaveChangesAsync(cancellationToken);
    }

    // Compatibility guard for internal callers of the retired custom protocol.
    public Task<ExchangeLaunchCodeResponse> ExchangeAsync(ExchangeLaunchCodeRequest request, CancellationToken cancellationToken)
        => Task.FromException<ExchangeLaunchCodeResponse>(new MiniAppException("LEGACY_PROTOCOL_REMOVED", "Use the OpenID Connect token endpoint.", 410));

    private IQueryable<MiniApp> AppQuery() => db.MiniApps.Include(app => app.Category).Include(app => app.Developer)
        .Include(app => app.PermissionMappings).ThenInclude(mapping => mapping.Permission);
    private IQueryable<MiniApp> ActiveAppQuery() => AppQuery().Where(app => app.Status == MiniAppStatus.Active && app.DeletedAt == null && app.Developer.Status == DeveloperStatus.Active && app.PublishedVersion > 0 && app.PublishedConfigurationJson != null);
    private static MiniAppPermissionDto ToPermission(MiniAppPermission permission) => new(permission.Code, permission.Name, permission.Description, permission.IsSensitive);
    private static MiniAppDetailDto ToDetail(MiniApp app) => new(app.Id, app.Name, app.Slug, app.Description, app.IconUrl, app.CoverUrl, app.Developer.Name, app.PermissionMappings.Select(mapping => ToPermission(mapping.Permission)).OrderBy(permission => permission.Code).ToArray(), app.Status.ToString());

    private async Task<LaunchMiniAppResponse> FailLaunch(Guid accountId, Guid appId, string code, string message, CancellationToken cancellationToken)
    {
        db.MiniAppLaunchLogs.Add(new MiniAppLaunchLog { AccountId = accountId, MiniAppId = appId, Status = MiniAppLaunchStatus.Failed, FailureReason = code });
        await db.SaveChangesAsync(cancellationToken);
        throw new MiniAppException(code, message, 403);
    }

}
