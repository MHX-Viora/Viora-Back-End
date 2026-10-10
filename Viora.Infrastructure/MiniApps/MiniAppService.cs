using Microsoft.EntityFrameworkCore;
using Viora.Application.MiniApps;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.MiniApps;

public sealed partial class MiniAppService(AppDbContext db, IClientCredentialService credentials) : IMiniAppService
{
    private const int LaunchCodeLifetimeSeconds = 60;

    public async Task<MiniAppListResponse> GetActiveAsync(CancellationToken cancellationToken)
    {
        var apps = await ActiveAppQuery().AsNoTracking().OrderByDescending(app => app.IsFeatured).ThenBy(app => app.Name).ToListAsync(cancellationToken);
        var categories = await db.MiniAppCategories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
        var items = apps.Select(app => { var c = MiniAppConfigurationSnapshot.Published(app); return new MiniAppListItemDto(app.Id, c.Name, c.Slug, c.Description, c.IconUrl, c.CoverUrl, c.IsFeatured, app.Developer.Name, c.CategoryId, c.CategoryId is Guid categoryId ? categories.GetValueOrDefault(categoryId) : null, c.AuthenticationMode, app.PublishedVersion); }).ToArray();
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
        return new(app.Id, c.Name, c.Slug, c.Description, c.IconUrl, c.CoverUrl, app.Developer.Name, await ApprovedPermissions(app, cancellationToken), app.Status.ToString(), c.WebUrl, c.AllowedDomains, bridgeOrigins, c.CallbackUrls, c.CategoryId, category, c.AuthenticationMode, app.PublishedVersion);
    }

    public async Task<LaunchMiniAppResponse> LaunchAsync(Guid accountId, Guid miniAppId, CancellationToken cancellationToken)
    {
        var app = await AppQuery().SingleOrDefaultAsync(item => item.Id == miniAppId && item.DeletedAt == null, cancellationToken);
        if (app is null) throw new MiniAppException(MiniAppErrorCodes.NotFound, "Mini App không tồn tại.", 404);
        if (app.Developer.Status != DeveloperStatus.Active)
            return await FailLaunch(accountId, app.Id, MiniAppErrorCodes.DeveloperSuspended, "Developer hiện đang bị tạm ngừng.", cancellationToken);
        if (app.Status == MiniAppStatus.Suspended)
            return await FailLaunch(accountId, app.Id, MiniAppErrorCodes.Suspended, "Mini App hiện đang tạm ngừng hoạt động.", cancellationToken);
        if (app.Status != MiniAppStatus.Active)
            return await FailLaunch(accountId, app.Id, MiniAppErrorCodes.NotActive, "Mini App chưa hoạt động.", cancellationToken);

        if (!await db.Accounts.AnyAsync(a => a.Id == accountId && a.Status == AccountStatus.Active && a.DeletedAt == null, cancellationToken)) throw new MiniAppException("ACCOUNT_NOT_ACTIVE", "Active account required.", 403);
        var configuration = MiniAppConfigurationSnapshot.Published(app);
        var domains = MiniAppSecurityPolicy.NormalizeDomains(configuration.AllowedDomains);
        if (!MiniAppSecurityPolicy.IsAllowedHttpsUrl(configuration.WebUrl, domains))
            return await FailLaunch(accountId, app.Id, MiniAppErrorCodes.DomainNotAllowed, "Invalid registered domain.", cancellationToken);
        var token = credentials.CreateLaunchCode();
        var session = new MiniAppRuntimeSession { MiniAppId = app.Id, AccountId = accountId, TokenHash = credentials.HashLaunchCode(token), ExpiresAt = DateTime.UtcNow.AddMinutes(30) };
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

    public async Task<ExchangeLaunchCodeResponse> ExchangeAsync(ExchangeLaunchCodeRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ClientId) || request.ClientId.Length > 80 || string.IsNullOrWhiteSpace(request.Code) || request.Code.Length > 200 || request.ClientSecret?.Length > 200 || request.State?.Length > 512 || request.RedirectUri?.Length > 2048 || request.CodeVerifier?.Length > 128)
            throw new MiniAppException(MiniAppErrorCodes.InvalidClient, "Invalid authentication request.", 400);
        var app = await AppQuery().SingleOrDefaultAsync(item => item.ClientId == request.ClientId && item.DeletedAt == null, cancellationToken);
        if (app is null || !ClientAuthenticated(app, request))
        {
            await AuditExchangeFailure(null, MiniAppErrorCodes.InvalidClient, cancellationToken);
            throw new MiniAppException(MiniAppErrorCodes.InvalidClient, "Client credentials không hợp lệ.", 401);
        }
        if (app.Developer.Status != DeveloperStatus.Active)
            throw new MiniAppException(MiniAppErrorCodes.DeveloperSuspended, "Developer hiện đang bị tạm ngừng.", 403);
        if (app.Status != MiniAppStatus.Active)
            throw new MiniAppException(app.Status == MiniAppStatus.Suspended ? MiniAppErrorCodes.Suspended : MiniAppErrorCodes.NotActive, "Mini App chưa hoạt động.", 403);

        var now = DateTime.UtcNow;
        var codeHash = credentials.HashLaunchCode(request.Code);
        var launchCode = await db.MiniAppLaunchCodes.AsNoTracking().SingleOrDefaultAsync(code => code.CodeHash == codeHash && code.MiniAppId == app.Id, cancellationToken);
        if (launchCode is null)
            throw await ExchangeFailure(app.Id, MiniAppErrorCodes.InvalidLaunchCode, "Launch code không hợp lệ.", cancellationToken);
        if (launchCode.UsedAt is not null)
            throw await ExchangeFailure(app.Id, MiniAppErrorCodes.LaunchCodeUsed, "Launch code đã được sử dụng.", cancellationToken);
        if (launchCode.ExpiresAt <= now)
            throw await ExchangeFailure(app.Id, MiniAppErrorCodes.LaunchCodeExpired, "Launch code đã hết hạn.", cancellationToken);

        var configuration = MiniAppConfigurationSnapshot.Published(app);
        if (configuration.AuthenticationMode != "AnktSso" || launchCode.RedirectUri is null || launchCode.StateHash is null || launchCode.CodeChallenge is null ||
            request.RedirectUri != launchCode.RedirectUri || !MiniAppSecurityPolicy.IsExactRedirect(request.RedirectUri ?? "", configuration.CallbackUrls ?? []) ||
            credentials.HashLaunchCode(request.State ?? "") != launchCode.StateHash || !MiniAppSecurityPolicy.ValidatePkce(request.CodeVerifier, launchCode.CodeChallenge))
            throw await ExchangeFailure(app.Id, "INVALID_AUTH_TRANSACTION", "Redirect, state or PKCE does not match.", cancellationToken);
        if (launchCode.RuntimeSessionId is null || !await db.MiniAppRuntimeSessions.AnyAsync(s => s.Id == launchCode.RuntimeSessionId && s.MiniAppId == app.Id && s.AccountId == launchCode.AccountId && s.ClosedAt == null && s.ExpiresAt > now, cancellationToken))
            throw new MiniAppException("SESSION_EXPIRED", "Runtime session expired.", 401);
        var grantedBeforeConsume = await GrantedPermissions(launchCode.AccountId, app, cancellationToken);
        if (!grantedBeforeConsume.Contains("identity.login")) throw new MiniAppException(MiniAppErrorCodes.ConsentRequired, "Login consent was revoked.", 403);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Serialize first pairwise-identity creation even when distinct valid codes
        // for the same app/account are redeemed concurrently.
        if (db.Database.IsNpgsql())
        {
            var lockBytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.ASCII.GetBytes($"{app.Id:N}:{launchCode.AccountId:N}"));
            var identityLock = BitConverter.ToInt64(lockBytes, 0);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({identityLock})", cancellationToken);
        }
        var redeemTime = DateTime.UtcNow;
        var consumed = await db.MiniAppLaunchCodes
            .Where(code => code.Id == launchCode.Id && code.UsedAt == null && code.ExpiresAt > redeemTime &&
                db.MiniApps.Any(a => a.Id == app.Id && a.Status == MiniAppStatus.Active && a.DeletedAt == null && a.Developer.Status == DeveloperStatus.Active) &&
                db.Accounts.Any(a => a.Id == launchCode.AccountId && a.Status == AccountStatus.Active && a.DeletedAt == null) &&
                db.MiniAppRuntimeSessions.Any(s => s.Id == launchCode.RuntimeSessionId && s.ClosedAt == null && s.ExpiresAt > redeemTime))
            .ExecuteUpdateAsync(setters => setters.SetProperty(code => code.UsedAt, redeemTime), cancellationToken);
        if (consumed != 1)
            throw await ExchangeFailure(app.Id, MiniAppErrorCodes.LaunchCodeUsed, "Launch code không còn khả dụng.", cancellationToken);

        var account = await db.Accounts.AsNoTracking().Include(account => account.User)
            .SingleOrDefaultAsync(account => account.Id == launchCode.AccountId && account.Status == AccountStatus.Active && account.DeletedAt == null, cancellationToken)
            ?? throw new MiniAppException(MiniAppErrorCodes.InvalidLaunchCode, "Tài khoản không còn khả dụng.", 401);
        var scopes = await GrantedPermissions(account.Id, app, cancellationToken);
        if (!scopes.Contains("identity.login", StringComparer.Ordinal)) throw new MiniAppException(MiniAppErrorCodes.ConsentRequired, "Identity permission was revoked.", 403);

        string? subject = null;
        if (scopes.Contains("identity.login", StringComparer.Ordinal))
        {
            var identity = await db.MiniAppExternalIdentities.SingleOrDefaultAsync(item => item.AccountId == account.Id && item.MiniAppId == app.Id, cancellationToken);
            if (identity is null)
            {
                identity = new MiniAppExternalIdentity { AccountId = account.Id, MiniAppId = app.Id, Subject = credentials.CreateSubject() };
                db.MiniAppExternalIdentities.Add(identity);
            }
            subject = identity.Subject;
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var profile = MiniAppSecurityPolicy.ProjectProfile(subject ?? string.Empty, account.User?.DisplayName, account.User?.AvatarUrl, account.Email, account.Phone, scopes);
        return new(profile.Subject, profile.DisplayName, profile.AvatarUrl, profile.Email, profile.Phone, profile.Scopes);
    }

    private IQueryable<MiniApp> AppQuery() => db.MiniApps.Include(app => app.Category).Include(app => app.Developer)
        .Include(app => app.PermissionMappings).ThenInclude(mapping => mapping.Permission);
    private IQueryable<MiniApp> ActiveAppQuery() => AppQuery().Where(app => app.Status == MiniAppStatus.Active && app.DeletedAt == null && app.Developer.Status == DeveloperStatus.Active);
    private static MiniAppPermissionDto ToPermission(MiniAppPermission permission) => new(permission.Code, permission.Name, permission.Description, permission.IsSensitive);
    private static MiniAppDetailDto ToDetail(MiniApp app) => new(app.Id, app.Name, app.Slug, app.Description, app.IconUrl, app.CoverUrl, app.Developer.Name, app.PermissionMappings.Select(mapping => ToPermission(mapping.Permission)).OrderBy(permission => permission.Code).ToArray(), app.Status.ToString());

    private async Task<LaunchMiniAppResponse> FailLaunch(Guid accountId, Guid appId, string code, string message, CancellationToken cancellationToken)
    {
        db.MiniAppLaunchLogs.Add(new MiniAppLaunchLog { AccountId = accountId, MiniAppId = appId, Status = MiniAppLaunchStatus.Failed, FailureReason = code });
        await db.SaveChangesAsync(cancellationToken);
        throw new MiniAppException(code, message, 403);
    }

    private async Task<MiniAppException> ExchangeFailure(Guid appId, string code, string message, CancellationToken cancellationToken)
    {
        await AuditExchangeFailure(appId, code, cancellationToken);
        return new MiniAppException(code, message, 400);
    }

    private async Task AuditExchangeFailure(Guid? appId, string code, CancellationToken cancellationToken)
    {
        db.MiniAppAuditLogs.Add(new MiniAppAuditLog { MiniAppId = appId, Action = "ExchangeFailed", Detail = code });
        await db.SaveChangesAsync(cancellationToken);
    }
}
