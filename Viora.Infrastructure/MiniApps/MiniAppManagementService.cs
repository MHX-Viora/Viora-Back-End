using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Viora.Application.MiniApps;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.MiniApps;

public sealed partial class MiniAppManagementService(AppDbContext db, IClientCredentialService credentials) : IMiniAppManagementService, IDeveloperMiniAppService
{
    public async Task<MiniAppAdminDashboard> GetDashboardAsync(CancellationToken cancellationToken) => new(
        await db.MiniApps.CountAsync(app => app.DeletedAt == null, cancellationToken),
        await db.MiniApps.CountAsync(app => app.Status == MiniAppStatus.Active && app.DeletedAt == null, cancellationToken),
        await db.MiniApps.CountAsync(app => app.Status == MiniAppStatus.PendingReview && app.DeletedAt == null, cancellationToken),
        await db.MiniApps.CountAsync(app => app.Status == MiniAppStatus.Suspended && app.DeletedAt == null, cancellationToken),
        await db.Developers.CountAsync(cancellationToken),
        await db.MiniAppLaunchLogs.CountAsync(log => log.Status == MiniAppLaunchStatus.Succeeded, cancellationToken),
        await db.MiniAppLaunchLogs.CountAsync(log => log.Status == MiniAppLaunchStatus.Failed, cancellationToken));

    public async Task<MiniAppAdminPage<MiniAppAdminListItem>> GetAppsAsync(int page, int pageSize, string? search, MiniAppStatus? status, CancellationToken cancellationToken)
    {
        (page, pageSize) = NormalizePage(page, pageSize);
        var query = db.MiniApps.AsNoTracking().Where(app => app.DeletedAt == null);
        if (status is not null) query = query.Where(app => app.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(app => app.Name.ToLower().Contains(term) || app.Slug.ToLower().Contains(term) || app.Developer.Name.ToLower().Contains(term));
        }
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(app => app.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(app => new MiniAppAdminListItem(app.Id, app.Name, app.Slug, app.Developer.Name, app.WebUrl, app.Status.ToString(), app.IsFeatured, app.CreatedAt))
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
        var app = await db.MiniApps.SingleOrDefaultAsync(item => item.Id == id && item.DeletedAt == null, cancellationToken)
            ?? throw new MiniAppException(MiniAppErrorCodes.NotFound, "Mini App không tồn tại.", 404);
        var allowed = status switch
        {
            MiniAppStatus.Active => app.Status is MiniAppStatus.PendingReview or MiniAppStatus.Suspended,
            MiniAppStatus.Rejected => app.Status == MiniAppStatus.PendingReview,
            MiniAppStatus.Suspended => app.Status == MiniAppStatus.Active,
            _ => false
        };
        if (!allowed) throw new MiniAppException("INVALID_STATUS_TRANSITION", "Chuyển trạng thái không hợp lệ.", 409);
        app.Status = status;
        db.MiniAppAuditLogs.Add(new MiniAppAuditLog { ActorAccountId = actorAccountId, MiniAppId = app.Id, DeveloperId = app.DeveloperId, Action = status switch { MiniAppStatus.Active => "MiniAppApproved", MiniAppStatus.Rejected => "MiniAppRejected", _ => "MiniAppSuspended" }, Detail = SafeDetail(reason) });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAppAsync(Guid actorAccountId, Guid id, MiniAppConfigurationInput input, CancellationToken cancellationToken)
    {
        var app = await AppQuery().SingleOrDefaultAsync(item => item.Id == id && item.DeletedAt == null, cancellationToken)
            ?? throw new MiniAppException(MiniAppErrorCodes.NotFound, "Mini App không tồn tại.", 404);
        await ApplyInput(app, input, true, cancellationToken);
        db.MiniAppAuditLogs.Add(new MiniAppAuditLog { ActorAccountId = actorAccountId, MiniAppId = app.Id, DeveloperId = app.DeveloperId, Action = "PermissionChanged" });
        await db.SaveChangesAsync(cancellationToken);
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
        if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.Email)) throw new MiniAppException("VALIDATION_ERROR", "Tên và email Developer là bắt buộc.");
        var developer = new Developer { AccountId = input.AccountId, Name = input.Name.Trim(), CompanyName = input.CompanyName?.Trim(), Email = input.Email.Trim().ToLowerInvariant(), Phone = input.Phone?.Trim(), Website = input.Website?.Trim(), Status = DeveloperStatus.Pending };
        db.Developers.Add(developer);
        db.MiniAppAuditLogs.Add(new MiniAppAuditLog { ActorAccountId = actorAccountId, DeveloperId = developer.Id, Action = "DeveloperCreated" });
        await db.SaveChangesAsync(cancellationToken);
        return new(developer.Id, developer.AccountId, developer.Name, developer.CompanyName, developer.Email, developer.Phone, developer.Website, developer.Status.ToString(), 0, developer.CreatedAt, developer.UpdatedAt);
    }

    public async Task SetDeveloperStatusAsync(Guid actorAccountId, Guid id, DeveloperStatus status, CancellationToken cancellationToken)
    {
        var developer = await db.Developers.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new MiniAppException("DEVELOPER_NOT_FOUND", "Developer không tồn tại.", 404);
        if (status is not (DeveloperStatus.Active or DeveloperStatus.Suspended or DeveloperStatus.Rejected)) throw new MiniAppException("INVALID_STATUS_TRANSITION", "Trạng thái Developer không hợp lệ.", 409);
        developer.Status = status;
        db.MiniAppAuditLogs.Add(new MiniAppAuditLog { ActorAccountId = actorAccountId, DeveloperId = id, Action = status == DeveloperStatus.Active ? "DeveloperApproved" : "DeveloperSuspended" });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MiniAppPermissionDto>> GetPermissionsAsync(CancellationToken cancellationToken) =>
        await db.MiniAppPermissions.AsNoTracking()
            .Where(permission => permission.Status == MiniAppPermissionStatus.Active).OrderBy(permission => permission.Code)
            .Select(permission => new MiniAppPermissionDto(permission.Code, permission.Name, permission.Description, permission.IsSensitive))
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
        var developer = await db.Developers.SingleOrDefaultAsync(item => item.AccountId == accountId, cancellationToken)
            ?? throw new MiniAppException("DEVELOPER_NOT_FOUND", "Tài khoản chưa được liên kết Developer.", 403);
        if (developer.Status != DeveloperStatus.Active) throw new MiniAppException(MiniAppErrorCodes.DeveloperSuspended, "Developer chưa hoạt động.", 403);
        var secret = credentials.CreateSecret();
        var app = new MiniApp { DeveloperId = developer.Id, ClientId = credentials.CreateClientId(), ClientSecretHash = credentials.HashSecret(secret), Status = MiniAppStatus.Draft };
        db.MiniApps.Add(app);
        await ApplyInput(app, input, false, cancellationToken);
        db.MiniAppAuditLogs.Add(new MiniAppAuditLog { ActorAccountId = accountId, DeveloperId = developer.Id, MiniAppId = app.Id, Action = "MiniAppCreated" });
        await db.SaveChangesAsync(cancellationToken);
        return new(app.Id, app.ClientId, secret);
    }

    public async Task<IReadOnlyList<MiniAppDeveloperView>> GetOwnedAsync(Guid accountId, CancellationToken cancellationToken) =>
        (await AppQuery().AsNoTracking().Where(app => app.Developer.AccountId == accountId && app.DeletedAt == null).OrderByDescending(app => app.CreatedAt).ToListAsync(cancellationToken)).Select(ToView).ToArray();

    public async Task<MiniAppDeveloperView?> GetOwnedAsync(Guid accountId, Guid id, CancellationToken cancellationToken)
    {
        var app = await AppQuery().AsNoTracking().SingleOrDefaultAsync(item => item.Id == id && item.Developer.AccountId == accountId && item.DeletedAt == null, cancellationToken);
        return app is null ? null : ToView(app);
    }

    public async Task UpdateOwnedAsync(Guid accountId, Guid id, MiniAppConfigurationInput input, CancellationToken cancellationToken)
    {
        var app = await OwnedEditable(accountId, id, cancellationToken);
        await ApplyInput(app, input with { IsFeatured = app.IsFeatured }, false, cancellationToken);
        if (app.Status == MiniAppStatus.Rejected) app.Status = MiniAppStatus.Draft;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SubmitReviewAsync(Guid accountId, Guid id, CancellationToken cancellationToken)
    {
        var app = await OwnedEditable(accountId, id, cancellationToken);
        if (app.Status != MiniAppStatus.Draft) throw new MiniAppException("INVALID_STATUS_TRANSITION", "Chỉ Mini App Draft mới được gửi duyệt.", 409);
        ValidateUrls(app);
        app.Status = MiniAppStatus.PendingReview;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<MiniAppCredentialResponse> RotateSecretAsync(Guid accountId, Guid id, CancellationToken cancellationToken)
    {
        var app = await AppQuery().SingleOrDefaultAsync(item => item.Id == id && item.Developer.AccountId == accountId && item.DeletedAt == null, cancellationToken)
            ?? throw new MiniAppException(MiniAppErrorCodes.NotFound, "Mini App không tồn tại.", 404);
        var secret = credentials.CreateSecret();
        app.ClientSecretHash = credentials.HashSecret(secret);
        app.SecretRotatedAt = DateTime.UtcNow;
        db.MiniAppAuditLogs.Add(new MiniAppAuditLog { ActorAccountId = accountId, MiniAppId = app.Id, DeveloperId = app.DeveloperId, Action = "SecretRotated" });
        await db.SaveChangesAsync(cancellationToken);
        return new(app.Id, app.ClientId, secret);
    }

    public async Task<MiniAppDeveloperView> GetPartnerConfigurationAsync(string clientId, string clientSecret, CancellationToken cancellationToken)
    {
        var app = await AppQuery().AsNoTracking().SingleOrDefaultAsync(item => item.ClientId == clientId && item.DeletedAt == null, cancellationToken);
        if (app is null || !credentials.VerifySecret(clientSecret, app.ClientSecretHash)) throw new MiniAppException(MiniAppErrorCodes.InvalidClient, "Client credentials không hợp lệ.", 401);
        return ToView(app);
    }

    private IQueryable<MiniApp> AppQuery() => db.MiniApps.Include(app => app.Developer).Include(app => app.PermissionMappings).ThenInclude(mapping => mapping.Permission);

    private async Task<MiniApp> OwnedEditable(Guid accountId, Guid id, CancellationToken cancellationToken)
    {
        var app = await AppQuery().SingleOrDefaultAsync(item => item.Id == id && item.Developer.AccountId == accountId && item.DeletedAt == null, cancellationToken)
            ?? throw new MiniAppException(MiniAppErrorCodes.NotFound, "Mini App không tồn tại.", 404);
        if (app.Status is MiniAppStatus.PendingReview or MiniAppStatus.Active or MiniAppStatus.Suspended) throw new MiniAppException("MINI_APP_NOT_EDITABLE", "Mini App không thể sửa ở trạng thái hiện tại.", 409);
        return app;
    }

    private async Task ApplyInput(MiniApp app, MiniAppConfigurationInput input, bool allowFeatured, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.Slug) || !SlugPattern().IsMatch(input.Slug)) throw new MiniAppException("VALIDATION_ERROR", "Tên hoặc slug không hợp lệ.");
        var domains = MiniAppSecurityPolicy.NormalizeDomains(input.AllowedDomains);
        app.Name = input.Name.Trim(); app.Slug = input.Slug.Trim().ToLowerInvariant(); app.Description = input.Description?.Trim();
        app.IconUrl = input.IconUrl?.Trim(); app.CoverUrl = input.CoverUrl?.Trim(); app.WebUrl = input.WebUrl.Trim(); app.CallbackUrl = input.CallbackUrl.Trim();
        app.AllowedDomains = domains; if (allowFeatured) app.IsFeatured = input.IsFeatured;
        ValidateUrls(app);

        var codes = input.Permissions.Distinct(StringComparer.Ordinal).ToArray();
        var permissions = await db.MiniAppPermissions.Where(permission => codes.Contains(permission.Code) && permission.Status == MiniAppPermissionStatus.Active).ToListAsync(cancellationToken);
        if (permissions.Count != codes.Length) throw new MiniAppException(MiniAppErrorCodes.PermissionDenied, "Permission không tồn tại hoặc chưa hoạt động.", 422);
        var approvedIds = permissions.Select(permission => permission.Id).ToHashSet();
        db.MiniAppPermissionMappings.RemoveRange(app.PermissionMappings.Where(mapping => !approvedIds.Contains(mapping.PermissionId)));
        var existingIds = app.PermissionMappings.Select(mapping => mapping.PermissionId).ToHashSet();
        foreach (var permission in permissions.Where(permission => !existingIds.Contains(permission.Id)))
        {
            app.PermissionMappings.Add(new MiniAppPermissionMapping { MiniApp = app, MiniAppId = app.Id, Permission = permission, PermissionId = permission.Id });
        }
    }

    private static void ValidateUrls(MiniApp app)
    {
        if (app.AllowedDomains.Length == 0 || !MiniAppSecurityPolicy.IsAllowedHttpsUrl(app.WebUrl, app.AllowedDomains) || !MiniAppSecurityPolicy.IsAllowedHttpsUrl(app.CallbackUrl, app.AllowedDomains))
            throw new MiniAppException(MiniAppErrorCodes.DomainNotAllowed, "WebUrl/CallbackUrl phải dùng HTTPS và thuộc AllowedDomains.", 422);
    }

    private static MiniAppDeveloperView ToView(MiniApp app) => new(app.Id, app.Name, app.Slug, app.Developer.Name, app.Description, app.IconUrl, app.CoverUrl, app.WebUrl, app.CallbackUrl, app.AllowedDomains, app.PermissionMappings.Select(mapping => new MiniAppPermissionDto(mapping.Permission.Code, mapping.Permission.Name, mapping.Permission.Description, mapping.Permission.IsSensitive)).OrderBy(permission => permission.Code).ToArray(), app.ClientId, app.Status.ToString(), app.IsFeatured, app.CreatedAt, app.UpdatedAt);
    private static (int Page, int PageSize) NormalizePage(int page, int pageSize) => (Math.Max(1, page), Math.Clamp(pageSize, 1, 100));
    private static string? SafeDetail(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, 500)];
    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();
}
