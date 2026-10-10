using Microsoft.EntityFrameworkCore;
using Viora.Application.MiniApps;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.MiniApps;

public sealed partial class MiniAppService
{
    private bool ClientAuthenticated(MiniApp app, ExchangeLaunchCodeRequest request)
    {
        var configuration = MiniAppConfigurationSnapshot.Published(app);
        return configuration.ClientAuthenticationMethod == "None" || credentials.VerifySecret(request.ClientSecret ?? "", app.ClientSecretHash);
    }

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
        return app is null ? new(false, [], session.ExpiresAt) : new(true, await GrantedPermissions(accountId, app, ct), session.ExpiresAt);
    }

    public async Task CloseSessionAsync(Guid accountId, Guid miniAppId, ValidateMiniAppSessionRequest request, CancellationToken ct)
    {
        var hash = credentials.HashLaunchCode(request.SessionToken);
        await db.MiniAppRuntimeSessions.Where(s => s.Id == request.SessionId && s.MiniAppId == miniAppId && s.AccountId == accountId && s.TokenHash == hash && s.ClosedAt == null)
            .ExecuteUpdateAsync(update => update.SetProperty(s => s.ClosedAt, DateTime.UtcNow), ct);
    }

    public async Task<LaunchMiniAppResponse> AuthorizeAsync(Guid accountId, Guid miniAppId, AuthorizeMiniAppRequest request, CancellationToken ct)
    {
        var validation = await ValidateSessionAsync(accountId, miniAppId, new(request.SessionId, request.SessionToken), ct);
        if (!validation.Active) throw new MiniAppException("SESSION_EXPIRED", "A valid active runtime session is required.", 401);
        var app = await ActiveAppQuery().AsNoTracking().SingleAsync(a => a.Id == miniAppId, ct);
        var configuration = MiniAppConfigurationSnapshot.Published(app);
        if (configuration.AuthenticationMode != "AnktSso") throw new MiniAppException("SSO_NOT_ENABLED", "Independent apps manage their own login.", 403);
        if (request.RedirectUri?.Length > 2048 || !MiniAppSecurityPolicy.IsExactRedirect(request.RedirectUri ?? "", configuration.CallbackUrls ?? []))
            throw new MiniAppException("INVALID_REDIRECT_URI", "Exact registered redirect URI required.", 422);
        var callbackHost = new Uri(request.RedirectUri!).IdnHost;
        if (!await db.MiniAppVerifiedDomains.AnyAsync(d => d.MiniAppId == miniAppId && d.Host == callbackHost && d.VerifiedAt != null, ct))
            throw new MiniAppException("DOMAIN_NOT_VERIFIED", "Verify the callback domain before SSO authorization.", 403);
        if (string.IsNullOrWhiteSpace(request.State) || request.State.Length is < 16 or > 512 || request.CodeChallengeMethod != "S256" || request.CodeChallenge is not { Length: 43 } || request.CodeChallenge.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
            throw new MiniAppException("INVALID_AUTH_TRANSACTION", "State and S256 PKCE challenge required.", 422);
        var permissions = await ApprovedPermissions(app, ct);
        var missing = permissions.Where(p => !validation.Permissions.Contains(p.Code)).ToArray();
        if (!validation.Permissions.Contains("identity.login")) return new(true, missing, null, null, configuration.AllowedDomains);
        var rawCode = credentials.CreateLaunchCode();
        db.MiniAppLaunchCodes.Add(new MiniAppLaunchCode { AccountId = accountId, MiniAppId = miniAppId, CodeHash = credentials.HashLaunchCode(rawCode),
            ExpiresAt = DateTime.UtcNow.AddSeconds(LaunchCodeLifetimeSeconds), RedirectUri = request.RedirectUri, StateHash = credentials.HashLaunchCode(request.State), CodeChallenge = request.CodeChallenge, RuntimeSessionId = request.SessionId });
        db.MiniAppAuditLogs.Add(new MiniAppAuditLog { MiniAppId = miniAppId, ActorAccountId = accountId, Action = "AuthorizationIssued" });
        await db.SaveChangesAsync(ct);
        return new(false, [], MiniAppSecurityPolicy.BuildLaunchUrl(request.RedirectUri!, rawCode, request.State), LaunchCodeLifetimeSeconds, configuration.AllowedDomains);
    }

    public async Task ReportAsync(Guid accountId, Guid miniAppId, MiniAppReportInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 1000) throw new MiniAppException("VALIDATION_ERROR", "Report reason required, at most 1000 characters.", 422);
        if (!await db.MiniApps.AnyAsync(a => a.Id == miniAppId && a.DeletedAt == null, ct)) throw new MiniAppException(MiniAppErrorCodes.NotFound, "App not found.", 404);
        db.MiniAppReports.Add(new MiniAppReport { MiniAppId = miniAppId, AccountId = accountId, Reason = input.Reason.Trim() }); await db.SaveChangesAsync(ct);
    }
}
