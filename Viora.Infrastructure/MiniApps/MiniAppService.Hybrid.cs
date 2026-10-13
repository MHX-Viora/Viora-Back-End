using Microsoft.EntityFrameworkCore;
using Viora.Application.MiniApps;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.MiniApps;

public sealed partial class MiniAppService
{
    private async Task<IReadOnlyList<MiniAppPermissionDto>> ApprovedPermissions(MiniApp app, CancellationToken ct)
    {
        var codes = MiniAppConfigurationSnapshot.Published(app).Permissions;
        return await db.MiniAppPermissions.AsNoTracking().Where(p => codes.Contains(p.Code) && MiniAppSecurityPolicy.SupportedPermissionCodes.Contains(p.Code) && p.Status == MiniAppPermissionStatus.Active)
            .OrderBy(p => p.Code).Select(p => new MiniAppPermissionDto(p.Code, p.Name, p.Description, p.IsSensitive, true)).ToListAsync(ct);
    }

    private async Task<List<string>> GrantedPermissions(Guid accountId, MiniApp app, CancellationToken ct)
    {
        var codes = MiniAppConfigurationSnapshot.Published(app).Permissions;
        return await db.MiniAppUserConsents.AsNoTracking().Where(c => c.AccountId == accountId && c.MiniAppId == app.Id && c.Granted && c.RevokedAt == null &&
                c.Permission.Status == MiniAppPermissionStatus.Active && codes.Contains(c.Permission.Code) && MiniAppSecurityPolicy.SupportedPermissionCodes.Contains(c.Permission.Code))
            .Select(c => c.Permission.Code).Distinct().ToListAsync(ct);
    }

    public async Task<ValidateMiniAppSessionResponse> ValidateSessionAsync(Guid accountId, Guid miniAppId, ValidateMiniAppSessionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.SessionToken) || request.SessionToken.Length > 200) return new(false, [], null);
        var hash = credentials.HashLaunchCode(request.SessionToken);
        var session = await db.MiniAppRuntimeSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == request.SessionId && s.MiniAppId == miniAppId && s.AccountId == accountId && s.TokenHash == hash &&
            s.ExpiresAt > DateTime.UtcNow && s.ClosedAt == null && s.Account.Status == AccountStatus.Active && s.Account.DeletedAt == null, ct);
        if (session is null) return new(false, [], null);
        var app = await ActiveAppQuery().AsNoTracking().SingleOrDefaultAsync(a => a.Id == miniAppId, ct);
        return app is null || app.PublishedVersion != session.PublishedVersion ? new(false, [], session.ExpiresAt) : new(true, await GrantedPermissions(accountId, app, ct), session.ExpiresAt);
    }

    public async Task CloseSessionAsync(Guid accountId, Guid miniAppId, ValidateMiniAppSessionRequest request, CancellationToken ct)
    {
        var hash = credentials.HashLaunchCode(request.SessionToken);
        await db.MiniAppRuntimeSessions.Where(s => s.Id == request.SessionId && s.MiniAppId == miniAppId && s.AccountId == accountId && s.TokenHash == hash && s.ClosedAt == null)
            .ExecuteUpdateAsync(update => update.SetProperty(s => s.ClosedAt, DateTime.UtcNow), ct);
    }

    // Retired even for internal callers; runtime SSO uses OpenIddict exclusively.
    public Task<LaunchMiniAppResponse> AuthorizeAsync(Guid accountId, Guid miniAppId, AuthorizeMiniAppRequest request, CancellationToken ct)
        => Task.FromException<LaunchMiniAppResponse>(new MiniAppException("LEGACY_PROTOCOL_REMOVED", "Use OpenID Connect authorization.", 410));

    public async Task ReportAsync(Guid accountId, Guid miniAppId, MiniAppReportInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 1000) throw new MiniAppException("VALIDATION_ERROR", "Report reason required, at most 1000 characters.", 422);
        if (!await db.MiniApps.AnyAsync(a => a.Id == miniAppId && a.DeletedAt == null, ct)) throw new MiniAppException(MiniAppErrorCodes.NotFound, "App not found.", 404);
        db.MiniAppReports.Add(new MiniAppReport { MiniAppId = miniAppId, AccountId = accountId, Reason = input.Reason.Trim() }); await db.SaveChangesAsync(ct);
    }
}
