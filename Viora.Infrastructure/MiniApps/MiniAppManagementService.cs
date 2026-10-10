using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Viora.Application.MiniApps;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.MiniApps;

public sealed partial class MiniAppManagementService(AppDbContext db, IClientCredentialService credentials, IMiniAppDomainVerifier? domainVerifier = null) : IMiniAppManagementService, IDeveloperMiniAppService
{
    public async Task<MiniAppAdminDashboard> GetDashboardAsync(CancellationToken cancellationToken) => new(
        await db.MiniApps.CountAsync(app => app.DeletedAt == null, cancellationToken),
        await db.MiniApps.CountAsync(app => app.Status == MiniAppStatus.Active && app.DeletedAt == null, cancellationToken),
        await db.MiniApps.CountAsync(app => (app.Status == MiniAppStatus.PendingReview || app.PendingVersion != null) && app.DeletedAt == null, cancellationToken),
        await db.MiniApps.CountAsync(app => app.Status == MiniAppStatus.Suspended && app.DeletedAt == null, cancellationToken),
        await db.Developers.CountAsync(cancellationToken),
        await db.MiniAppLaunchLogs.CountAsync(log => log.Status == MiniAppLaunchStatus.Succeeded, cancellationToken),
        await db.MiniAppLaunchLogs.CountAsync(log => log.Status == MiniAppLaunchStatus.Failed, cancellationToken));

    public async Task<MiniAppAdminPage<MiniAppAdminListItem>> GetAppsAsync(int page, int pageSize, string? search, MiniAppStatus? status, CancellationToken cancellationToken)
    {
        (page, pageSize) = NormalizePage(page, pageSize);
        var query = db.MiniApps.AsNoTracking().Where(app => app.DeletedAt == null);
        if (status == MiniAppStatus.PendingReview) query = query.Where(app => app.Status == MiniAppStatus.PendingReview || app.PendingVersion != null);
        else if (status is not null) query = query.Where(app => app.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(app => app.Name.ToLower().Contains(term) || app.Slug.ToLower().Contains(term) || app.Developer.Name.ToLower().Contains(term));
        }
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(app => app.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(app => new MiniAppAdminListItem(app.Id, app.Name, app.Slug, app.Developer.Name, app.WebUrl, app.Status.ToString(), app.IsFeatured, app.CreatedAt, app.PendingVersion))
            .ToListAsync(cancellationToken);
        return new(items, total, page, pageSize);
    }

    public async Task<MiniAppDeveloperView?> GetAppAsync(Guid id, CancellationToken cancellationToken)
    {
        var app = await AppQuery().AsNoTracking().SingleOrDefaultAsync(item => item.Id == id && item.DeletedAt == null, cancellationToken);
        return app is null ? null : ToView(app);
    }

    public async Task SetAppStatusAsync(Guid actorAccountId, Guid id, MiniAppStatus status, string? reason, CancellationToken cancellationToken)
    {
        var app = await AppQuery().SingleOrDefaultAsync(item => item.Id == id && item.DeletedAt == null, cancellationToken)
            ?? throw new MiniAppException(MiniAppErrorCodes.NotFound, "Mini App không tồn tại.", 404);
        var previousStatus = app.Status;
        var allowed = status switch
        {
            MiniAppStatus.Active => app.Status is MiniAppStatus.PendingReview or MiniAppStatus.Suspended || app.PendingVersion != null,
            MiniAppStatus.Rejected => app.PendingVersion != null,
            MiniAppStatus.Suspended => app.Status == MiniAppStatus.Active,
            MiniAppStatus.Archived => app.Status is MiniAppStatus.Active or MiniAppStatus.Suspended,
            _ => false
        };
        if (!allowed) throw new MiniAppException("INVALID_STATUS_TRANSITION", "Chuyển trạng thái không hợp lệ.", 409);
        if (status == MiniAppStatus.Rejected && string.IsNullOrWhiteSpace(reason)) throw new MiniAppException("VALIDATION_ERROR", "A rejection reason is required.", 422);
        if (status == MiniAppStatus.Active && app.Developer.Status != DeveloperStatus.Active) throw new MiniAppException(MiniAppErrorCodes.DeveloperSuspended, "Developer must be active before approval or restoration.", 403);
        if (app.PendingVersion is not null && (status == MiniAppStatus.Rejected || status == MiniAppStatus.Active && app.Status != MiniAppStatus.Suspended))
        {
            var version = app.Versions.Single(v => v.Version == app.PendingVersion && v.Status == MiniAppStatus.PendingReview);
            if (status == MiniAppStatus.Active)
            {
                await RequireVerifiedDomains(app, System.Text.Json.JsonSerializer.Deserialize<MiniAppConfigurationInput>(version.ConfigurationJson)!, cancellationToken);
                app.PublishedConfigurationJson = version.ConfigurationJson; app.PublishedVersion = version.Version;
            }
            version.Status = status; version.Reason = SafeDetail(reason); version.ReviewedAt = DateTime.UtcNow; version.ReviewedByAccountId = actorAccountId;
            app.PendingVersion = null;
            app.Status = app.PublishedVersion > 0 ? app.Status == MiniAppStatus.Suspended ? MiniAppStatus.Suspended : MiniAppStatus.Active : status;
        }
        else app.Status = status;
        db.MiniAppAuditLogs.Add(new MiniAppAuditLog { ActorAccountId = actorAccountId, MiniAppId = app.Id, DeveloperId = app.DeveloperId, Action = status switch { MiniAppStatus.Active when previousStatus == MiniAppStatus.Suspended => "MiniAppRestored", MiniAppStatus.Active => "MiniAppApproved", MiniAppStatus.Rejected => "MiniAppRejected", MiniAppStatus.Archived => "MiniAppArchived", _ => "MiniAppSuspended" }, Detail = SafeDetail(reason) });
        await SaveAsync(cancellationToken);
    }

    public async Task UpdateAppAsync(Guid actorAccountId, Guid id, MiniAppConfigurationInput input, CancellationToken cancellationToken)
    {
        var app = await AppQuery().SingleOrDefaultAsync(item => item.Id == id && item.DeletedAt == null, cancellationToken)
            ?? throw new MiniAppException(MiniAppErrorCodes.NotFound, "Mini App không tồn tại.", 404);
        if (app.PendingVersion != null) throw new MiniAppException("REVIEW_PENDING", "A submitted version is immutable.", 409);
        await PreserveLegacyPublished(app, cancellationToken);
        await ApplyInput(app, input, true, cancellationToken);
        db.MiniAppAuditLogs.Add(new MiniAppAuditLog { ActorAccountId = actorAccountId, MiniAppId = app.Id, DeveloperId = app.DeveloperId, Action = "MiniAppUpdated" });
        await SaveAsync(cancellationToken);
    }

    public async Task<MiniAppAdminPage<DeveloperDto>> GetDevelopersAsync(int page, int pageSize, string? search, DeveloperStatus? status, CancellationToken cancellationToken)
    {
        (page, pageSize) = NormalizePage(page, pageSize);
        var query = db.Developers.AsNoTracking();
        if (status is not null) query = query.Where(developer => developer.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(developer => developer.Name.ToLower().Contains(term) || developer.Email.ToLower().Contains(term) || (developer.CompanyName != null && developer.CompanyName.ToLower().Contains(term)));
        }
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(developer => developer.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(developer => new DeveloperDto(developer.Id, developer.AccountId, developer.Name, developer.CompanyName, developer.Email, developer.Phone, developer.Website, developer.Status.ToString(), developer.MiniApps.Count, developer.CreatedAt, developer.UpdatedAt))
            .ToListAsync(cancellationToken);
        return new(items, total, page, pageSize);
    }

    public async Task<DeveloperDto> CreateDeveloperAsync(Guid actorAccountId, DeveloperInput input, CancellationToken cancellationToken)
    {
        ValidateDeveloper(input);
        if (await db.Developers.AnyAsync(d => d.Email == input.Email.Trim().ToLowerInvariant(), cancellationToken)) throw new MiniAppException("DEVELOPER_EXISTS", "Developer email already registered.", 409);
        if (input.AccountId is Guid owner && (!await db.Accounts.AnyAsync(a => a.Id == owner && a.DeletedAt == null && a.Status == AccountStatus.Active, cancellationToken) ||
            await db.Developers.AnyAsync(d => d.AccountId == owner, cancellationToken) || await db.DeveloperMemberships.AnyAsync(m => m.AccountId == owner, cancellationToken))) throw new MiniAppException("MEMBERSHIP_EXISTS", "Owner must be an available active account.", 409);
        var developer = new Developer { AccountId = input.AccountId, Name = input.Name.Trim(), CompanyName = input.CompanyName?.Trim(), Email = input.Email.Trim().ToLowerInvariant(), Phone = input.Phone?.Trim(), Website = input.Website?.Trim(), Status = DeveloperStatus.Pending };
        db.Developers.Add(developer);
        if (input.AccountId is Guid ownerId) db.DeveloperMemberships.Add(new DeveloperMembership { DeveloperId = developer.Id, AccountId = ownerId, Role = DeveloperMembershipRole.Owner });
        db.MiniAppAuditLogs.Add(new MiniAppAuditLog { ActorAccountId = actorAccountId, DeveloperId = developer.Id, Action = "DeveloperCreated" });
        await SaveAsync(cancellationToken);
        return new(developer.Id, developer.AccountId, developer.Name, developer.CompanyName, developer.Email, developer.Phone, developer.Website, developer.Status.ToString(), 0, developer.CreatedAt, developer.UpdatedAt);
    }

    public async Task SetDeveloperStatusAsync(Guid actorAccountId, Guid id, DeveloperStatus status, CancellationToken cancellationToken)
    {
        var developer = await db.Developers.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new MiniAppException("DEVELOPER_NOT_FOUND", "Developer không tồn tại.", 404);
        var allowed = (developer.Status, status) switch
        {
            (DeveloperStatus.Pending, DeveloperStatus.Active or DeveloperStatus.Rejected) => true,
            (DeveloperStatus.Active, DeveloperStatus.Suspended) => true,
            (DeveloperStatus.Suspended, DeveloperStatus.Active) => true,
            _ => false
        };
        if (!allowed) throw new MiniAppException("INVALID_STATUS_TRANSITION", "Trạng thái Developer không hợp lệ. Hồ sơ bị từ chối cần được gửi lại trước khi duyệt.", 409);
        var previousStatus = developer.Status;
        developer.Status = status;
        db.MiniAppAuditLogs.Add(new MiniAppAuditLog { ActorAccountId = actorAccountId, DeveloperId = id, Action = status switch { DeveloperStatus.Rejected => "DeveloperRejected", DeveloperStatus.Suspended => "DeveloperSuspended", _ when previousStatus == DeveloperStatus.Suspended => "DeveloperRestored", _ => "DeveloperApproved" } });
        await SaveAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MiniAppPermissionDto>> GetPermissionsAsync(CancellationToken cancellationToken) =>
        await db.MiniAppPermissions.AsNoTracking()
            .Where(permission => permission.Status == MiniAppPermissionStatus.Active && MiniAppSecurityPolicy.SupportedPermissionCodes.Contains(permission.Code)).OrderBy(permission => permission.Code)
            .Select(permission => new MiniAppPermissionDto(permission.Code, permission.Name, permission.Description, permission.IsSensitive, true))
            .ToListAsync(cancellationToken);

    public async Task<MiniAppAdminPage<MiniAppAuditDto>> GetAuditLogsAsync(int page, int pageSize, Guid? miniAppId, CancellationToken cancellationToken)
    {
        (page, pageSize) = NormalizePage(page, pageSize);
        var query = db.MiniAppAuditLogs.AsNoTracking();
        if (miniAppId is not null) query = query.Where(log => log.MiniAppId == miniAppId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(log => log.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(log => new MiniAppAuditDto(log.Id, log.MiniAppId, log.DeveloperId, log.ActorAccountId, log.Action, log.Detail, log.CreatedAt))
            .ToListAsync(cancellationToken);
        return new(items, total, page, pageSize);
    }

    public async Task<MiniAppCredentialResponse> CreateAsync(Guid accountId, MiniAppConfigurationInput input, CancellationToken cancellationToken)
    {
        var developer = await RequireDeveloper(accountId, false, cancellationToken);
        if (developer.Status != DeveloperStatus.Active) throw new MiniAppException(MiniAppErrorCodes.DeveloperSuspended, "Developer chưa hoạt động.", 403);
        var secret = credentials.CreateSecret();
        var app = new MiniApp { DeveloperId = developer.Id, ClientId = credentials.CreateClientId(), ClientSecretHash = credentials.HashSecret(secret), Status = MiniAppStatus.Draft };
        db.MiniApps.Add(app);
        await ApplyInput(app, input, false, cancellationToken);
        db.MiniAppAuditLogs.Add(new MiniAppAuditLog { ActorAccountId = accountId, DeveloperId = developer.Id, MiniAppId = app.Id, Action = "MiniAppCreated" });
        await SaveAsync(cancellationToken);
        return new(app.Id, app.ClientId, secret);
    }

    public async Task<IReadOnlyList<MiniAppDeveloperView>> GetOwnedAsync(Guid accountId, CancellationToken cancellationToken) =>
        (await AppQuery().AsNoTracking().Where(app => (app.Developer.AccountId == accountId || db.DeveloperMemberships.Any(m => m.DeveloperId == app.DeveloperId && m.AccountId == accountId)) && app.DeletedAt == null).OrderByDescending(app => app.CreatedAt).ToListAsync(cancellationToken)).Select(ToView).ToArray();

    public async Task<MiniAppDeveloperView?> GetOwnedAsync(Guid accountId, Guid id, CancellationToken cancellationToken)
    {
        var app = await AppQuery().AsNoTracking().SingleOrDefaultAsync(item => item.Id == id && (item.Developer.AccountId == accountId || db.DeveloperMemberships.Any(m => m.DeveloperId == item.DeveloperId && m.AccountId == accountId)) && item.DeletedAt == null, cancellationToken);
        return app is null ? null : ToView(app);
    }

    public async Task UpdateOwnedAsync(Guid accountId, Guid id, MiniAppConfigurationInput input, CancellationToken cancellationToken)
    {
        var app = await OwnedEditable(accountId, id, cancellationToken);
        await PreserveLegacyPublished(app, cancellationToken);
        await ApplyInput(app, input with { IsFeatured = app.IsFeatured }, false, cancellationToken);
        if (app.Status == MiniAppStatus.Rejected) app.Status = MiniAppStatus.Draft;
        Audit(accountId, app.DeveloperId, id, "MiniAppUpdated");
        await SaveAsync(cancellationToken);
    }

    public async Task SubmitReviewAsync(Guid accountId, Guid id, CancellationToken cancellationToken)
    {
        var app = await OwnedEditable(accountId, id, cancellationToken);
        if (app.Status is not (MiniAppStatus.Draft or MiniAppStatus.Active or MiniAppStatus.Rejected)) throw new MiniAppException("INVALID_STATUS_TRANSITION", "App cannot be submitted.", 409);
        ValidateUrls(app);
        await PreserveLegacyPublished(app, cancellationToken);
        await RequireVerifiedDomains(app, MiniAppConfigurationSnapshot.Draft(app), cancellationToken);
        var next = app.Versions.Count == 0 ? 1 : app.Versions.Max(v => v.Version) + 1;
        var submitted = new MiniAppVersion { MiniAppId = app.Id, Version = next, ConfigurationJson = MiniAppConfigurationSnapshot.Serialize(app) };
        db.MiniAppVersions.Add(submitted);
        app.PendingVersion = next;
        if (app.PublishedVersion == 0) app.Status = MiniAppStatus.PendingReview;
        Audit(accountId, app.DeveloperId, id, "VersionSubmitted", next.ToString());
        await SaveAsync(cancellationToken);
    }

    public async Task<MiniAppCredentialResponse> RotateSecretAsync(Guid accountId, Guid id, CancellationToken cancellationToken)
    {
        var app = await AppQuery().SingleOrDefaultAsync(item => item.Id == id && (item.Developer.AccountId == accountId || db.DeveloperMemberships.Any(m => m.DeveloperId == item.DeveloperId && m.AccountId == accountId)) && item.DeletedAt == null, cancellationToken)
            ?? throw new MiniAppException(MiniAppErrorCodes.NotFound, "Mini App không tồn tại.", 404);
        await RequireDeveloper(accountId, true, cancellationToken);
        var secret = credentials.CreateSecret();
        app.ClientSecretHash = credentials.HashSecret(secret);
        app.SecretRotatedAt = DateTime.UtcNow;
        db.MiniAppAuditLogs.Add(new MiniAppAuditLog { ActorAccountId = accountId, MiniAppId = app.Id, DeveloperId = app.DeveloperId, Action = "SecretRotated" });
        await SaveAsync(cancellationToken);
        return new(app.Id, app.ClientId, secret);
    }

    public async Task<MiniAppDeveloperView> GetPartnerConfigurationAsync(string clientId, string clientSecret, CancellationToken cancellationToken)
    {
        var app = await AppQuery().AsNoTracking().SingleOrDefaultAsync(item => item.ClientId == clientId && item.DeletedAt == null, cancellationToken);
        if (app is null || !credentials.VerifySecret(clientSecret, app.ClientSecretHash)) throw new MiniAppException(MiniAppErrorCodes.InvalidClient, "Client credentials không hợp lệ.", 401);
        return ToView(app);
    }

    private IQueryable<MiniApp> AppQuery() => db.MiniApps.Include(app => app.Developer).Include(app => app.Category).Include(app => app.Versions).Include(app => app.PermissionMappings).ThenInclude(mapping => mapping.Permission);

    private async Task<MiniApp> OwnedEditable(Guid accountId, Guid id, CancellationToken cancellationToken)
    {
        var app = await AppQuery().SingleOrDefaultAsync(item => item.Id == id && (item.Developer.AccountId == accountId || db.DeveloperMemberships.Any(m => m.DeveloperId == item.DeveloperId && m.AccountId == accountId)) && item.DeletedAt == null, cancellationToken)
            ?? throw new MiniAppException(MiniAppErrorCodes.NotFound, "Mini App không tồn tại.", 404);
        if (app.PendingVersion != null || app.Status is MiniAppStatus.PendingReview or MiniAppStatus.Suspended or MiniAppStatus.Archived) throw new MiniAppException("MINI_APP_NOT_EDITABLE", "Mini App không thể sửa ở trạng thái hiện tại.", 409);
        await RequireDeveloper(accountId, false, cancellationToken);
        return app;
    }

    private async Task ApplyInput(MiniApp app, MiniAppConfigurationInput input, bool allowFeatured, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 120 || string.IsNullOrWhiteSpace(input.Slug) || input.Slug.Length > 120 || !SlugPattern().IsMatch(input.Slug) || input.Description?.Length > 1000 ||
            string.IsNullOrWhiteSpace(input.WebUrl) || input.WebUrl.Length > 2048 || input.CallbackUrl?.Length > 2048 || input.IconUrl?.Length > 2048 || input.CoverUrl?.Length > 2048 ||
            input.AllowedDomains is null || input.AllowedDomains.Count is < 1 or > 20 || input.Permissions is null || input.Permissions.Count > 30 || input.CallbackUrls?.Count > 20 || input.AllowedOrigins?.Count > 20)
            throw new MiniAppException("VALIDATION_ERROR", "Invalid Mini App configuration.", 422);
        var domains = MiniAppSecurityPolicy.NormalizeDomains(input.AllowedDomains);
        if (domains.Length != input.AllowedDomains.Distinct(StringComparer.OrdinalIgnoreCase).Count() || domains.Any(d => !MiniAppSecurityPolicy.IsPublicHost(d) || d.Contains('*')) || !MiniAppSecurityPolicy.IsAllowedHttpsUrl(input.WebUrl, domains))
            throw new MiniAppException(MiniAppErrorCodes.DomainNotAllowed, "Exact public HTTPS domains are required.", 422);
        foreach (var media in new[] { input.IconUrl, input.CoverUrl }.Where(v => !string.IsNullOrWhiteSpace(v)))
            if (!Uri.TryCreate(media, UriKind.Absolute, out var mediaUri) || !MiniAppSecurityPolicy.IsAllowedHttpsUrl(media, [mediaUri.IdnHost]))
                throw new MiniAppException("VALIDATION_ERROR", "Images must use public HTTPS URLs.", 422);
        if (await db.MiniApps.AnyAsync(a => a.Id != app.Id && a.Slug == input.Slug, cancellationToken)) throw new MiniAppException("SLUG_EXISTS", "Mini App slug is already registered.", 409);
        app.Name = input.Name.Trim(); app.Slug = input.Slug.Trim().ToLowerInvariant(); app.Description = input.Description?.Trim();
        app.IconUrl = input.IconUrl?.Trim(); app.CoverUrl = input.CoverUrl?.Trim(); app.WebUrl = input.WebUrl.Trim(); app.CallbackUrl = input.CallbackUrl?.Trim() ?? "";
        if (!Enum.TryParse<MiniAppAuthenticationMode>(input.AuthenticationMode, out var mode) || !Enum.IsDefined(mode) ||
            !Enum.TryParse<MiniAppClientAuthenticationMethod>(input.ClientAuthenticationMethod, out var authMethod) || !Enum.IsDefined(authMethod))
            throw new MiniAppException("VALIDATION_ERROR", "Invalid authentication configuration.", 422);
        app.AuthenticationMode = mode; app.ClientAuthenticationMethod = authMethod;
        app.CallbackUrls = (input.CallbackUrls ?? (string.IsNullOrWhiteSpace(app.CallbackUrl) ? [] : [app.CallbackUrl])).Distinct(StringComparer.Ordinal).ToArray();
        app.AllowedOrigins = (input.AllowedOrigins ?? [new Uri(app.WebUrl).GetLeftPart(UriPartial.Authority)]).Distinct(StringComparer.Ordinal).ToArray();
        app.CategoryId = input.CategoryId;
        if (app.CategoryId != null && !await db.MiniAppCategories.AnyAsync(c => c.Id == app.CategoryId && c.IsActive, cancellationToken)) throw new MiniAppException("VALIDATION_ERROR", "Invalid category.", 422);
        if (mode == MiniAppAuthenticationMode.Independent && input.Permissions.Any(c => c.StartsWith("identity.", StringComparison.Ordinal) || c.StartsWith("profile.", StringComparison.Ordinal))) throw new MiniAppException("PERMISSION_DENIED", "Independent apps cannot request identity scopes.", 422);
        if (mode == MiniAppAuthenticationMode.AnktSso && !input.Permissions.Contains("identity.login")) throw new MiniAppException("PERMISSION_DENIED", "SSO requires identity.login.", 422);
        app.AllowedDomains = domains; if (allowFeatured) app.IsFeatured = input.IsFeatured;
        ValidateUrls(app);

        var codes = input.Permissions.Distinct(StringComparer.Ordinal).ToArray();
        if (codes.Any(code => !MiniAppSecurityPolicy.SupportedPermissionCodes.Contains(code))) throw new MiniAppException(MiniAppErrorCodes.PermissionDenied, "Requested capability is not implemented by ANKT.", 422);
        var permissions = await db.MiniAppPermissions.Where(permission => codes.Contains(permission.Code) && permission.Status == MiniAppPermissionStatus.Active).ToListAsync(cancellationToken);
        if (permissions.Count != codes.Length) throw new MiniAppException(MiniAppErrorCodes.PermissionDenied, "Permission không tồn tại hoặc chưa hoạt động.", 422);
        var approvedIds = permissions.Select(permission => permission.Id).ToHashSet();
        var removed = app.PermissionMappings.Where(mapping => !approvedIds.Contains(mapping.PermissionId)).ToArray();
        db.MiniAppPermissionMappings.RemoveRange(removed);
        foreach (var mapping in removed) app.PermissionMappings.Remove(mapping);
        var existingIds = app.PermissionMappings.Select(mapping => mapping.PermissionId).ToHashSet();
        foreach (var permission in permissions.Where(permission => !existingIds.Contains(permission.Id)))
        {
            app.PermissionMappings.Add(new MiniAppPermissionMapping { MiniApp = app, MiniAppId = app.Id, Permission = permission, PermissionId = permission.Id });
        }
    }

    private static void ValidateUrls(MiniApp app)
    {
        if (app.AllowedDomains.Length == 0 || !MiniAppSecurityPolicy.IsAllowedHttpsUrl(app.WebUrl, app.AllowedDomains) || app.AuthenticationMode == MiniAppAuthenticationMode.AnktSso && (app.CallbackUrls.Length == 0 || app.CallbackUrls.Any(url => !MiniAppSecurityPolicy.IsAllowedHttpsUrl(url, app.AllowedDomains) || !string.IsNullOrEmpty(new Uri(url).Fragment))) || app.AllowedOrigins.Any(origin => !MiniAppSecurityPolicy.IsAllowedHttpsUrl(origin, app.AllowedDomains) || new Uri(origin).GetLeftPart(UriPartial.Authority) != origin))
            throw new MiniAppException(MiniAppErrorCodes.DomainNotAllowed, "WebUrl/CallbackUrl phải dùng HTTPS và thuộc AllowedDomains.", 422);
    }

    private static MiniAppDeveloperView ToView(MiniApp app) => new(app.Id, app.Name, app.Slug, app.Developer.Name, app.Description, app.IconUrl, app.CoverUrl, app.WebUrl, app.CallbackUrl, app.AllowedDomains, app.PermissionMappings.Select(mapping => new MiniAppPermissionDto(mapping.Permission.Code, mapping.Permission.Name, mapping.Permission.Description, mapping.Permission.IsSensitive, mapping.Permission.Status == MiniAppPermissionStatus.Active)).OrderBy(permission => permission.Code).ToArray(), app.ClientId, app.Status.ToString(), app.IsFeatured, app.CreatedAt, app.UpdatedAt, app.CategoryId, app.Category?.Name, app.AuthenticationMode.ToString(), app.CallbackUrls, app.AllowedOrigins, app.ClientAuthenticationMethod.ToString(), app.PublishedVersion, app.PendingVersion);
    private static (int Page, int PageSize) NormalizePage(int page, int pageSize) => (Math.Max(1, page), Math.Clamp(pageSize, 1, 100));
    private static string? SafeDetail(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, 500)];
    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();
}
