using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using Viora.Application.MiniApps;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.MiniApps;

public sealed partial class MiniAppManagementService
{
    private async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new MiniAppException("CONCURRENT_UPDATE", "Configuration changed; reload and retry.", 409); }
    }
    public async Task<IReadOnlyList<MiniAppPermissionDto>> GetAdminPermissionsAsync(CancellationToken ct) => await db.MiniAppPermissions.AsNoTracking().OrderBy(p => p.Code).Select(p => new MiniAppPermissionDto(p.Code, p.Name, p.Description, p.IsSensitive, p.Status == MiniAppPermissionStatus.Active)).ToListAsync(ct);
    private async Task<Developer> RequireDeveloper(Guid accountId, bool ownerOnly, CancellationToken ct)
    {
        var developer = await db.Developers.SingleOrDefaultAsync(d => d.AccountId == accountId ||
            db.DeveloperMemberships.Any(m => m.DeveloperId == d.Id && m.AccountId == accountId && (!ownerOnly || m.Role == DeveloperMembershipRole.Owner)), ct)
            ?? throw new MiniAppException("DEVELOPER_NOT_FOUND", "Developer access denied.", 403);
        if (ownerOnly && developer.AccountId != accountId) throw new MiniAppException("OWNER_REQUIRED", "Only the owner can manage this resource.", 403);
        if (developer.Status != DeveloperStatus.Active) throw new MiniAppException(MiniAppErrorCodes.DeveloperSuspended, "Developer is not active.", 403);
        return developer;
    }

    public async Task<DeveloperProfileDto?> GetProfileAsync(Guid accountId, CancellationToken ct)
    {
        var d = await db.Developers.AsNoTracking().Include(d => d.MiniApps).SingleOrDefaultAsync(d => d.AccountId == accountId ||
            db.DeveloperMemberships.Any(m => m.DeveloperId == d.Id && m.AccountId == accountId), ct);
        return d is null ? null : Profile(d, accountId);
    }

    public async Task<DeveloperProfileDto> RegisterDeveloperAsync(Guid accountId, DeveloperInput input, CancellationToken ct)
    {
        if (await GetProfileAsync(accountId, ct) is not null) throw new MiniAppException("DEVELOPER_EXISTS", "Account already belongs to a developer.", 409);
        ValidateDeveloper(input);
        if (!await db.Accounts.AnyAsync(a => a.Id == accountId && a.Status == AccountStatus.Active && a.DeletedAt == null, ct)) throw new MiniAppException("ACCOUNT_NOT_FOUND", "Active account required.", 403);
        var email = input.Email.Trim().ToLowerInvariant();
        if (await db.Developers.AnyAsync(d => d.Email == email, ct)) throw new MiniAppException("DEVELOPER_EXISTS", "Developer email is already registered.", 409);
        var d = new Developer { AccountId = accountId, Name = input.Name.Trim(), CompanyName = input.CompanyName?.Trim(), Email = email, Phone = input.Phone?.Trim(), Website = input.Website?.Trim(), Status = DeveloperStatus.Pending };
        db.Developers.Add(d);
        db.DeveloperMemberships.Add(new DeveloperMembership { DeveloperId = d.Id, AccountId = accountId, Role = DeveloperMembershipRole.Owner });
        Audit(accountId, d.Id, null, "DeveloperRegistered");
        await SaveAsync(ct); return Profile(d, accountId);
    }

    public async Task<DeveloperProfileDto> UpdateProfileAsync(Guid accountId, DeveloperInput input, CancellationToken ct)
    {
        var d = await db.Developers.SingleOrDefaultAsync(d => d.AccountId == accountId, ct) ?? throw new MiniAppException("OWNER_REQUIRED", "Owner required.", 403);
        if (d.Status == DeveloperStatus.Suspended) throw new MiniAppException(MiniAppErrorCodes.DeveloperSuspended, "Developer suspended.", 403);
        ValidateDeveloper(input); var email = input.Email.Trim().ToLowerInvariant();
        if (await db.Developers.AnyAsync(x => x.Id != d.Id && x.Email == email, ct)) throw new MiniAppException("DEVELOPER_EXISTS", "Email already registered.", 409);
        var resubmitting = d.Status == DeveloperStatus.Rejected;
        d.Name = input.Name.Trim(); d.CompanyName = input.CompanyName?.Trim(); d.Email = email; d.Phone = input.Phone?.Trim(); d.Website = input.Website?.Trim();
        if (resubmitting) d.Status = DeveloperStatus.Pending;
        Audit(accountId, d.Id, null, resubmitting ? "DeveloperResubmitted" : "DeveloperProfileUpdated"); await SaveAsync(ct); return Profile(d, accountId);
    }

    private static void ValidateDeveloper(DeveloperInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 120 || string.IsNullOrWhiteSpace(input.Email) || input.Email.Length > 255 ||
            !MailAddress.TryCreate(input.Email.Trim(), out var address) || !string.Equals(address.Address, input.Email.Trim(), StringComparison.OrdinalIgnoreCase) || input.CompanyName?.Length > 160 || input.Phone?.Length > 20 || input.Website?.Length > 2048)
            throw new MiniAppException("VALIDATION_ERROR", "Tên Developer, email liên hệ hoặc độ dài thông tin không hợp lệ.", 422);
        if (!string.IsNullOrWhiteSpace(input.Website) && !MiniAppSecurityPolicy.IsAllowedHttpsUrl(input.Website, [Uri.TryCreate(input.Website, UriKind.Absolute, out var uri) ? uri.IdnHost : ""]))
            throw new MiniAppException("VALIDATION_ERROR", "Website must be public HTTPS.", 422);
    }
    private static DeveloperProfileDto Profile(Developer d, Guid accountId) => new(d.Id, d.AccountId, d.Name, d.CompanyName, d.Email, d.Phone, d.Website, d.Status.ToString(), d.MiniApps.Count, d.CreatedAt, d.UpdatedAt, d.AccountId == accountId ? "Owner" : "Member");
    private void Audit(Guid actor, Guid? developer, Guid? app, string action, string? detail = null) => db.MiniAppAuditLogs.Add(new MiniAppAuditLog { ActorAccountId = actor, DeveloperId = developer, MiniAppId = app, Action = action, Detail = SafeDetail(detail) });

    public async Task<IReadOnlyList<DeveloperTeamDto>> GetTeamAsync(Guid accountId, CancellationToken ct)
    {
        var d = await RequireDeveloper(accountId, false, ct);
        var members = await db.DeveloperMemberships.AsNoTracking().Where(m => m.DeveloperId == d.Id).Select(m => new DeveloperTeamDto(m.Id, m.AccountId, m.Role.ToString(), m.CreatedAt)).ToListAsync(ct);
        if (d.AccountId is Guid owner && !members.Any(m => m.AccountId == owner)) members.Insert(0, new(d.Id, owner, "Owner", d.CreatedAt));
        return members;
    }
    public async Task<DeveloperTeamDto> AddTeamAsync(Guid accountId, DeveloperTeamInput input, CancellationToken ct)
    {
        var d = await RequireDeveloper(accountId, true, ct);
        if (input.Role != "Member" || input.AccountId == accountId) throw new MiniAppException("VALIDATION_ERROR", "Only member invitations are supported.", 422);
        if (!await db.Accounts.AnyAsync(a => a.Id == input.AccountId && a.Status == AccountStatus.Active && a.DeletedAt == null, ct)) throw new MiniAppException("ACCOUNT_NOT_FOUND", "Account unavailable.", 404);
        if (await db.Developers.AnyAsync(x => x.AccountId == input.AccountId, ct) || await db.DeveloperMemberships.AnyAsync(m => m.AccountId == input.AccountId, ct)) throw new MiniAppException("MEMBERSHIP_EXISTS", "Account already belongs to a developer.", 409);
        var member = new DeveloperMembership { AccountId = input.AccountId, DeveloperId = d.Id, Role = DeveloperMembershipRole.Member };
        db.DeveloperMemberships.Add(member); Audit(accountId, d.Id, null, "TeamMemberAdded"); await SaveAsync(ct);
        return new(member.Id, member.AccountId, member.Role.ToString(), member.CreatedAt);
    }
    public async Task RemoveTeamAsync(Guid accountId, Guid membershipId, CancellationToken ct)
    {
        var d = await RequireDeveloper(accountId, true, ct);
        var member = await db.DeveloperMemberships.SingleOrDefaultAsync(m => m.Id == membershipId && m.DeveloperId == d.Id, ct) ?? throw new MiniAppException("MEMBERSHIP_NOT_FOUND", "Member not found.", 404);
        if (member.Role == DeveloperMembershipRole.Owner || member.AccountId == d.AccountId) throw new MiniAppException("OWNER_REQUIRED", "The owner cannot be removed.", 409);
        db.DeveloperMemberships.Remove(member); Audit(accountId, d.Id, null, "TeamMemberRemoved"); await SaveAsync(ct);
    }

    public async Task<IReadOnlyList<MiniAppDomainDto>> GetDomainsAsync(Guid accountId, Guid id, CancellationToken ct)
    {
        await RequireOwned(accountId, id, ct);
        return await db.MiniAppVerifiedDomains.AsNoTracking().Where(d => d.MiniAppId == id).OrderBy(d => d.Host).Select(d => new MiniAppDomainDto(d.Id, d.Host, d.VerifiedAt, d.ChallengeToken)).ToListAsync(ct);
    }
    public async Task<MiniAppDomainDto> AddDomainAsync(Guid accountId, Guid id, MiniAppDomainInput input, CancellationToken ct)
    {
        var app = await RequireOwned(accountId, id, ct);
        var hosts = MiniAppSecurityPolicy.NormalizeDomains([input.Host]);
        if (hosts.Length != 1 || !MiniAppSecurityPolicy.IsPublicHost(hosts[0]) || hosts[0].Contains('*')) throw new MiniAppException("VALIDATION_ERROR", "An exact public hostname is required.", 422);
        var domain = await db.MiniAppVerifiedDomains.SingleOrDefaultAsync(d => d.MiniAppId == id && d.Host == hosts[0], ct);
        if (domain is null)
        {
            domain = new MiniAppVerifiedDomain { MiniAppId = id, Host = hosts[0], ChallengeToken = credentials.CreateLaunchCode() };
            db.MiniAppVerifiedDomains.Add(domain); Audit(accountId, app.DeveloperId, id, "DomainChallengeCreated"); await SaveAsync(ct);
        }
        return new(domain.Id, domain.Host, domain.VerifiedAt, domain.ChallengeToken);
    }
    public async Task<MiniAppDomainDto> VerifyDomainAsync(Guid accountId, Guid id, Guid domainId, CancellationToken ct)
    {
        var app = await RequireOwned(accountId, id, ct);
        var domain = await db.MiniAppVerifiedDomains.SingleOrDefaultAsync(d => d.Id == domainId && d.MiniAppId == id, ct) ?? throw new MiniAppException("DOMAIN_NOT_FOUND", "Domain not found.", 404);
        var now = DateTime.UtcNow;
        var attempted = await db.MiniAppVerifiedDomains.Where(d => d.Id == domain.Id && (d.LastAttemptAt == null || d.LastAttemptAt < now.AddSeconds(-30)))
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.LastAttemptAt, now), ct);
        if (attempted != 1) throw new MiniAppException(MiniAppErrorCodes.RateLimited, "Wait before another verification attempt.", 429);
        var verified = domainVerifier is not null && await domainVerifier.VerifyAsync(domain.Host, domain.ChallengeToken, ct);
        domain.VerifiedAt = verified ? now : null; Audit(accountId, app.DeveloperId, id, verified ? "DomainVerified" : "DomainVerificationFailed"); await SaveAsync(ct);
        if (!verified) throw new MiniAppException("DOMAIN_VERIFICATION_FAILED", "Public HTTPS challenge did not match.", 422);
        return new(domain.Id, domain.Host, domain.VerifiedAt, domain.ChallengeToken);
    }
    private async Task RequireVerifiedDomains(MiniApp app, MiniAppConfigurationInput configuration, CancellationToken ct)
    {
        var activePermissions = await db.MiniAppPermissions.Where(p => configuration.Permissions.Contains(p.Code) && p.Status == MiniAppPermissionStatus.Active).CountAsync(ct);
        if (activePermissions != configuration.Permissions.Distinct().Count()) throw new MiniAppException(MiniAppErrorCodes.PermissionDenied, "A requested permission is no longer active.", 422);
        if (configuration.CategoryId is Guid categoryId && !await db.MiniAppCategories.AnyAsync(c => c.Id == categoryId && c.IsActive, ct)) throw new MiniAppException("CATEGORY_NOT_ACTIVE", "Category no longer active.", 422);
        var required = configuration.AllowedDomains.Concat((configuration.AllowedOrigins ?? []).Select(o => new Uri(o).IdnHost))
            .Concat((configuration.CallbackUrls ?? []).Select(o => new Uri(o).IdnHost)).Append(new Uri(configuration.WebUrl).IdnHost).Distinct(StringComparer.Ordinal).ToArray();
        var verified = await db.MiniAppVerifiedDomains.Where(d => d.MiniAppId == app.Id && d.VerifiedAt != null).Select(d => d.Host).ToListAsync(ct);
        if (required.Any(host => host.Contains('*') || !verified.Contains(host, StringComparer.Ordinal))) throw new MiniAppException("DOMAIN_NOT_VERIFIED", "Verify every registered domain before review.", 422);
    }
    private async Task<MiniApp> RequireOwned(Guid accountId, Guid id, CancellationToken ct)
    {
        var d = await RequireDeveloper(accountId, false, ct);
        return await AppQuery().SingleOrDefaultAsync(a => a.Id == id && a.DeveloperId == d.Id && a.DeletedAt == null, ct) ?? throw new MiniAppException(MiniAppErrorCodes.NotFound, "Mini App not found.", 404);
    }
    private Task PreserveLegacyPublished(MiniApp app, CancellationToken ct)
    {
        if (app.Status == MiniAppStatus.Active && app.PublishedConfigurationJson is null)
        {
            app.PublishedConfigurationJson = MiniAppConfigurationSnapshot.Serialize(app); app.PublishedVersion = 1;
            db.MiniAppVersions.Add(new MiniAppVersion { MiniAppId = app.Id, Version = 1, Status = MiniAppStatus.Active, ConfigurationJson = app.PublishedConfigurationJson, ReviewedAt = DateTime.UtcNow, Reason = "Imported published configuration" });
        }
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<MiniAppVersionDto>> GetVersionsAsync(Guid id, CancellationToken ct) => (await db.MiniAppVersions.AsNoTracking().Where(v => v.MiniAppId == id).OrderByDescending(v => v.Version).ToListAsync(ct)).Select(MiniAppConfigurationSnapshot.Version).ToArray();
    public async Task<IReadOnlyList<MiniAppVersionDto>> GetOwnedVersionsAsync(Guid accountId, Guid id, CancellationToken ct) { await RequireOwned(accountId, id, ct); return await GetVersionsAsync(id, ct); }
    public async Task<MiniAppAnalyticsDto> GetAnalyticsAsync(Guid accountId, Guid id, CancellationToken ct)
    {
        await RequireOwned(accountId, id, ct);
        var logs = await db.MiniAppLaunchLogs.AsNoTracking().Where(l => l.MiniAppId == id && l.CreatedAt >= DateTime.UtcNow.AddDays(-30)).ToListAsync(ct);
        return new(logs.Count(l => l.Status == MiniAppLaunchStatus.Succeeded), logs.Count(l => l.Status == MiniAppLaunchStatus.Failed), logs.Where(l => l.AccountId != null).Select(l => l.AccountId).Distinct().Count(),
            logs.GroupBy(l => l.CreatedAt.ToString("yyyy-MM-dd")).OrderBy(g => g.Key).Select(g => new MiniAppUsageDay(g.Key, g.Count(l => l.Status == MiniAppLaunchStatus.Succeeded), g.Count(l => l.Status == MiniAppLaunchStatus.Failed))).ToArray());
    }
    public async Task<MiniAppAdminPage<MiniAppAuditDto>> GetOwnedAuditAsync(Guid accountId, Guid id, int page, int pageSize, CancellationToken ct) { await RequireOwned(accountId, id, ct); return await GetAuditLogsAsync(page, pageSize, id, ct); }
    public async Task<IReadOnlyList<MiniAppCategoryDto>> GetCategoriesAsync(bool includeInactive, CancellationToken ct) => await db.MiniAppCategories.AsNoTracking().Where(c => includeInactive || c.IsActive).OrderBy(c => c.Name).Select(c => new MiniAppCategoryDto(c.Id, c.Name, c.Slug, c.IsActive)).ToListAsync(ct);
    public async Task<MiniAppCategoryDto> SaveCategoryAsync(Guid actorAccountId, Guid? id, MiniAppCategoryInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 120 || input.Slug.Length > 120 || !SlugPattern().IsMatch(input.Slug)) throw new MiniAppException("VALIDATION_ERROR", "Invalid category.", 422);
        var category = id is null ? new MiniAppCategory() : await db.MiniAppCategories.SingleOrDefaultAsync(c => c.Id == id, ct) ?? throw new MiniAppException("CATEGORY_NOT_FOUND", "Category not found.", 404);
        if (await db.MiniAppCategories.AnyAsync(c => c.Id != category.Id && c.Slug == input.Slug, ct)) throw new MiniAppException("CATEGORY_EXISTS", "Category slug exists.", 409);
        if (id is null) db.MiniAppCategories.Add(category); category.Name = input.Name.Trim(); category.Slug = input.Slug; category.IsActive = input.IsActive;
        Audit(actorAccountId, null, null, "CategoryUpdated"); await SaveAsync(ct); return new(category.Id, category.Name, category.Slug, category.IsActive);
    }
    public async Task SavePermissionAsync(Guid actorAccountId, string? code, MiniAppPermissionInput input, CancellationToken ct)
    {
        if (!MiniAppSecurityPolicy.SupportedPermissionCodes.Contains(input.Code)) throw new MiniAppException(MiniAppErrorCodes.PermissionDenied, "Implement the platform capability before enabling a scope.", 422);
        if (string.IsNullOrWhiteSpace(input.Code) || input.Code.Length > 100 || !System.Text.RegularExpressions.Regex.IsMatch(input.Code, "^[a-z][a-z0-9_]*(?:\\.[a-z][a-z0-9_]*)+$") || string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 160 || input.Description?.Length > 500 || code != null && code != input.Code)
            throw new MiniAppException("VALIDATION_ERROR", "Invalid permission.", 422);
        var permission = await db.MiniAppPermissions.SingleOrDefaultAsync(p => p.Code == (code ?? input.Code), ct);
        if (code is not null && permission is null) throw new MiniAppException("PERMISSION_NOT_FOUND", "Permission not found.", 404);
        if (code is null && permission is not null) throw new MiniAppException("PERMISSION_EXISTS", "Permission exists.", 409);
        if (permission is null) { permission = new MiniAppPermission { Code = input.Code }; db.MiniAppPermissions.Add(permission); }
        permission.Name = input.Name.Trim(); permission.Description = input.Description?.Trim(); permission.IsSensitive = input.IsSensitive; permission.Status = input.IsActive ? MiniAppPermissionStatus.Active : MiniAppPermissionStatus.Inactive;
        Audit(actorAccountId, null, null, "PermissionUpdated", input.Code); await SaveAsync(ct);
    }
    public async Task<MiniAppAdminPage<MiniAppReportDto>> GetReportsAsync(int page, int pageSize, CancellationToken ct)
    {
        (page, pageSize) = NormalizePage(page, pageSize); var query = db.MiniAppReports.AsNoTracking();
        var total = await query.CountAsync(ct); var items = await query.OrderByDescending(r => r.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).Select(r => new MiniAppReportDto(r.Id, r.MiniAppId, r.MiniApp.Name, r.Reason, r.Resolution, r.CreatedAt, r.ResolvedAt)).ToListAsync(ct); return new(items, total, page, pageSize);
    }
    public async Task ResolveReportAsync(Guid actorAccountId, Guid id, string reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500) throw new MiniAppException("VALIDATION_ERROR", "Resolution required.", 422);
        var report = await db.MiniAppReports.SingleOrDefaultAsync(r => r.Id == id, ct) ?? throw new MiniAppException("REPORT_NOT_FOUND", "Report not found.", 404);
        report.Resolution = reason.Trim(); report.ResolvedAt = DateTime.UtcNow; Audit(actorAccountId, null, report.MiniAppId, "ReportResolved", reason); await SaveAsync(ct);
    }
    public async Task<MiniAppCredentialResponse> CreateAdminAppAsync(Guid actorAccountId, Guid developerId, MiniAppConfigurationInput input, CancellationToken ct)
    {
        if (!await db.Developers.AnyAsync(d => d.Id == developerId && d.Status == DeveloperStatus.Active, ct)) throw new MiniAppException("DEVELOPER_NOT_FOUND", "Active developer required.", 422);
        var secret = credentials.CreateSecret(); var app = new MiniApp { DeveloperId = developerId, ClientId = credentials.CreateClientId(), ClientSecretHash = credentials.HashSecret(secret) };
        db.MiniApps.Add(app); await ApplyInput(app, input, true, ct); Audit(actorAccountId, developerId, app.Id, "MiniAppCreated"); await SaveAsync(ct); return new(app.Id, app.ClientId, secret);
    }
}
