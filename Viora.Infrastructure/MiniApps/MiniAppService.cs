using Microsoft.EntityFrameworkCore;
using Viora.Application.MiniApps;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.MiniApps;

public sealed class MiniAppService(AppDbContext db, IClientCredentialService credentials) : IMiniAppService
{
    private const int LaunchCodeLifetimeSeconds = 60;

    public async Task<MiniAppListResponse> GetActiveAsync(CancellationToken cancellationToken)
    {
        var items = await db.MiniApps.AsNoTracking()
            .Where(app => app.Status == MiniAppStatus.Active && app.DeletedAt == null && app.Developer.Status == DeveloperStatus.Active)
            .OrderByDescending(app => app.IsFeatured).ThenBy(app => app.Name)
            .Select(app => new MiniAppListItemDto(app.Id, app.Name, app.Slug, app.Description, app.IconUrl, app.CoverUrl, app.IsFeatured))
            .ToListAsync(cancellationToken);
        return new(items);
    }

    public async Task<MiniAppDetailDto?> GetActiveDetailAsync(Guid id, CancellationToken cancellationToken)
    {
        var app = await ActiveAppQuery().AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return app is null ? null : ToDetail(app);
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

        var domains = MiniAppSecurityPolicy.NormalizeDomains(app.AllowedDomains);
        if (!MiniAppSecurityPolicy.IsAllowedHttpsUrl(app.WebUrl, domains) ||
            !MiniAppSecurityPolicy.IsAllowedHttpsUrl(app.CallbackUrl, domains))
        {
            return await FailLaunch(accountId, app.Id, MiniAppErrorCodes.DomainNotAllowed, "Cấu hình domain Mini App không hợp lệ.", cancellationToken);
        }

        var permissionIds = app.PermissionMappings
            .Where(mapping => mapping.Permission.Status == MiniAppPermissionStatus.Active)
            .Select(mapping => mapping.PermissionId).ToArray();
        var grantedIds = await db.MiniAppUserConsents.AsNoTracking()
            .Where(consent => consent.AccountId == accountId && consent.MiniAppId == app.Id && consent.Granted && consent.RevokedAt == null)
            .Select(consent => consent.PermissionId).ToListAsync(cancellationToken);
        var missing = app.PermissionMappings
            .Where(mapping => permissionIds.Contains(mapping.PermissionId) && !grantedIds.Contains(mapping.PermissionId))
            .Select(mapping => ToPermission(mapping.Permission)).OrderBy(permission => permission.Code).ToArray();
        if (missing.Length > 0) return new(true, missing, null, null, null);

        var rawCode = credentials.CreateLaunchCode();
        db.MiniAppLaunchCodes.Add(new MiniAppLaunchCode
        {
            CodeHash = credentials.HashLaunchCode(rawCode), AccountId = accountId, MiniAppId = app.Id,
            ExpiresAt = DateTime.UtcNow.AddSeconds(LaunchCodeLifetimeSeconds)
        });
        db.MiniAppLaunchLogs.Add(new MiniAppLaunchLog { AccountId = accountId, MiniAppId = app.Id, Status = MiniAppLaunchStatus.Succeeded });
        await db.SaveChangesAsync(cancellationToken);
        return new(false, [], MiniAppSecurityPolicy.BuildLaunchUrl(app.CallbackUrl, rawCode), LaunchCodeLifetimeSeconds, domains);
    }

    public async Task GrantConsentAsync(Guid accountId, Guid miniAppId, GrantConsentRequest request, CancellationToken cancellationToken)
    {
        var requestedCodes = request.Permissions.Where(code => !string.IsNullOrWhiteSpace(code)).Distinct(StringComparer.Ordinal).ToArray();
        if (requestedCodes.Length == 0) throw new MiniAppException("VALIDATION_ERROR", "Cần chọn ít nhất một permission.", 422);
        var appAvailable = await db.MiniApps.AnyAsync(app => app.Id == miniAppId && app.DeletedAt == null && app.Status == MiniAppStatus.Active && app.Developer.Status == DeveloperStatus.Active, cancellationToken);
        if (!appAvailable) throw new MiniAppException(MiniAppErrorCodes.NotActive, "Mini App chưa hoạt động.", 403);
        var approved = await db.MiniAppPermissionMappings
            .Where(mapping => mapping.MiniAppId == miniAppId && requestedCodes.Contains(mapping.Permission.Code) && mapping.Permission.Status == MiniAppPermissionStatus.Active)
            .Select(mapping => new { mapping.PermissionId, mapping.Permission.Code }).ToListAsync(cancellationToken);
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
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeConsentAsync(Guid accountId, Guid miniAppId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        await db.MiniAppUserConsents
            .Where(consent => consent.AccountId == accountId && consent.MiniAppId == miniAppId && consent.Granted)
            .ExecuteUpdateAsync(setters => setters.SetProperty(consent => consent.Granted, false).SetProperty(consent => consent.RevokedAt, now), cancellationToken);
    }

    public async Task<ExchangeLaunchCodeResponse> ExchangeAsync(ExchangeLaunchCodeRequest request, CancellationToken cancellationToken)
    {
        var app = await AppQuery().SingleOrDefaultAsync(item => item.ClientId == request.ClientId && item.DeletedAt == null, cancellationToken);
        if (app is null || !credentials.VerifySecret(request.ClientSecret, app.ClientSecretHash))
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

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var consumed = await db.MiniAppLaunchCodes
            .Where(code => code.Id == launchCode.Id && code.UsedAt == null && code.ExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(code => code.UsedAt, now), cancellationToken);
        if (consumed != 1)
            throw await ExchangeFailure(app.Id, MiniAppErrorCodes.LaunchCodeUsed, "Launch code không còn khả dụng.", cancellationToken);

        var account = await db.Accounts.AsNoTracking().Include(account => account.User)
            .SingleOrDefaultAsync(account => account.Id == launchCode.AccountId && account.Status == AccountStatus.Active, cancellationToken)
            ?? throw new MiniAppException(MiniAppErrorCodes.InvalidLaunchCode, "Tài khoản không còn khả dụng.", 401);
        var scopes = await db.MiniAppUserConsents.AsNoTracking()
            .Where(consent => consent.AccountId == account.Id && consent.MiniAppId == app.Id && consent.Granted && consent.RevokedAt == null &&
                consent.Permission.Status == MiniAppPermissionStatus.Active &&
                db.MiniAppPermissionMappings.Any(mapping => mapping.MiniAppId == app.Id && mapping.PermissionId == consent.PermissionId))
            .Select(consent => consent.Permission.Code).Distinct().ToListAsync(cancellationToken);

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

    private IQueryable<MiniApp> AppQuery() => db.MiniApps.Include(app => app.Developer)
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
