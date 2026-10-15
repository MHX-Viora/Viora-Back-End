using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Viora.Application.MiniApps;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.MiniApps;

public sealed partial class MiniAppManagementService
{
    private static string ConfigurationHash(MiniApp app) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(MiniAppConfigurationSnapshot.Serialize(app))));
    private async Task NotifyOwner(MiniApp app, string title, string? content, CancellationToken ct)
    {
        if (app.Developer.AccountId is not Guid accountId) return;
        var userId = await db.Users.Where(u => u.AccountId == accountId).Select(u => (Guid?)u.Id).SingleOrDefaultAsync(ct);
        if (userId is Guid id) db.Notifications.Add(new Notification { UserId = id, NotificationType = NotificationType.System, Title = title, Content = content, ReferenceId = app.Id });
    }

    private async Task RequireActiveAccount(Guid accountId, CancellationToken ct)
    {
        if (!await db.Accounts.AnyAsync(a => a.Id == accountId && a.DeletedAt == null && a.Status == AccountStatus.Active, ct))
            throw new MiniAppException("ACCOUNT_UNAVAILABLE", "Tài khoản ANKT không còn hoạt động.", 403);
    }

    private async Task<DeveloperMembershipRole> RoleFor(Guid accountId, Developer developer, CancellationToken ct)
    {
        if (developer.AccountId == accountId) return DeveloperMembershipRole.Owner;
        return await db.DeveloperMemberships.Where(m => m.DeveloperId == developer.Id && m.AccountId == accountId)
            .Select(m => (DeveloperMembershipRole?)m.Role).SingleOrDefaultAsync(ct)
            ?? throw new MiniAppException("PERMISSION_DENIED", "Không có quyền truy cập tổ chức này.", 403);
    }

    private async Task RequireCapability(Guid accountId, Developer developer, string capability, CancellationToken ct)
    {
        await RequireActiveAccount(accountId, ct);
        var role = await RoleFor(accountId, developer, ct);
        var allowed = capability switch
        {
            "manage" => role is DeveloperMembershipRole.Owner or DeveloperMembershipRole.Admin,
            "edit" => role is DeveloperMembershipRole.Owner or DeveloperMembershipRole.Admin or DeveloperMembershipRole.Developer or DeveloperMembershipRole.Member,
            "analytics" => role is not DeveloperMembershipRole.Viewer,
            _ => true
        };
        if (!allowed) throw new MiniAppException("PERMISSION_DENIED", "Vai trò hiện tại không được thực hiện thao tác này.", 403);
    }

    public async Task<DeveloperTeamDto> UpdateTeamRoleAsync(Guid accountId, Guid membershipId, DeveloperTeamRoleInput input, CancellationToken ct)
    {
        var developer = await RequireDeveloper(accountId, false, ct);
        await RequireCapability(accountId, developer, "manage", ct);
        var membership = await db.DeveloperMemberships.SingleOrDefaultAsync(m => m.Id == membershipId && m.DeveloperId == developer.Id, ct)
            ?? throw new MiniAppException("MEMBERSHIP_NOT_FOUND", "Thành viên không tồn tại.", 404);
        if (!Enum.TryParse<DeveloperMembershipRole>(input.Role, out var role) || !Enum.IsDefined(role) || role == DeveloperMembershipRole.Owner)
            throw new MiniAppException("VALIDATION_ERROR", "Không hỗ trợ tự cấp hoặc chuyển quyền Owner.", 422);
        if (membership.AccountId == accountId || membership.AccountId == developer.AccountId || membership.Role == DeveloperMembershipRole.Owner)
            throw new MiniAppException("OWNER_REQUIRED", "Không được tự đổi vai trò hoặc thay đổi Owner.", 403);
        if (await RoleFor(accountId, developer, ct) == DeveloperMembershipRole.Admin && (role == DeveloperMembershipRole.Admin || membership.Role == DeveloperMembershipRole.Admin))
            throw new MiniAppException("OWNER_REQUIRED", "Chỉ Owner được quản lý vai trò Admin.", 403);
        membership.Role = role;
        Audit(accountId, developer.Id, null, "TeamRoleChanged", role.ToString());
        await SaveAsync(ct);
        return new(membership.Id, membership.AccountId, role.ToString(), membership.CreatedAt);
    }

    public async Task<MiniAppTechnicalTestDto?> GetTechnicalTestAsync(Guid accountId, Guid id, CancellationToken ct)
    {
        await RequireOwned(accountId, id, ct);
        var test = await db.MiniAppTechnicalTests.AsNoTracking().Where(t => t.MiniAppId == id).OrderByDescending(t => t.CreatedAt).FirstOrDefaultAsync(ct);
        return test is null ? null : JsonSerializer.Deserialize<MiniAppTechnicalTestDto>(test.ResultJson);
    }

    public async Task<MiniAppTechnicalTestDto> RunTechnicalTestAsync(Guid accountId, Guid id, CancellationToken ct)
    {
        var app = await RequireOwned(accountId, id, ct);
        await RequireAppCapability(accountId, app, "edit", ct);
        var hash = ConfigurationHash(app);
        var startedAt = DateTime.UtcNow;
        if (await db.MiniAppTechnicalTests.AnyAsync(t => t.MiniAppId == id && t.CreatedAt >= startedAt.AddSeconds(-30), ct))
            throw new MiniAppException("RATE_LIMITED", "Chờ 30 giây trước khi chạy lại kiểm thử kỹ thuật.", 429);
        var requestId = Guid.NewGuid().ToString("N");
        var attempt = new MiniAppTechnicalTest { MiniAppId = id, TestedByAccountId = accountId, ConfigurationHash = hash, Passed = false,
            ResultJson = JsonSerializer.Serialize(new MiniAppTechnicalTestDto(requestId, startedAt, hash, false, "Limited", [new("IN_PROGRESS", false, "Đang kiểm tra kết nối HTTPS.")])) };
        db.MiniAppTechnicalTests.Add(attempt);
        // Touch the concurrency token before network work, so simultaneous callers cannot both reserve a probe.
        app.UpdatedAt = startedAt;
        await SaveAsync(ct);
        var probe = technicalProbe ?? new MiniAppTechnicalProbe();
        var checks = new List<MiniAppTechnicalCheckDto>();
        try { ValidateUrls(app); checks.Add(new("URL_CONFIGURATION", true, "URL, origins và callback phù hợp cấu hình HTTPS đã đăng ký.")); }
        catch (MiniAppException) { checks.Add(new("URL_CONFIGURATION", false, "URL, origins hoặc callback không hợp lệ.")); }
        try { await RequireVerifiedDomains(app, MiniAppConfigurationSnapshot.Draft(app), ct); checks.Add(new("DOMAIN_AND_SCOPES", true, "Domain đã xác minh; quyền yêu cầu đang được hỗ trợ.")); }
        catch (MiniAppException) { checks.Add(new("DOMAIN_AND_SCOPES", false, "Xác minh domain và kiểm tra quyền đang hoạt động trước khi gửi duyệt.")); }
        checks.Add(new("SUBMISSION_METADATA", !string.IsNullOrWhiteSpace(app.SupportEmail) && !string.IsNullOrWhiteSpace(app.PrivacyPolicyUrl), "Cần email hỗ trợ và URL chính sách bảo mật để gửi duyệt."));
        checks.Add(await probe.CheckAsync("LAUNCH_HTTPS", app.WebUrl, ct));
        checks.Add(string.IsNullOrWhiteSpace(app.PrivacyPolicyUrl)
            ? new("PRIVACY_POLICY_HTTPS", false, "Chưa cấu hình URL chính sách bảo mật.")
            : await probe.CheckAsync("PRIVACY_POLICY_HTTPS", app.PrivacyPolicyUrl, ct));
        // No protocol token or synthetic identity is minted against production storage.
        var result = new MiniAppTechnicalTestDto(requestId, DateTime.UtcNow, hash, checks.All(c => c.Passed), "Limited", checks);
        attempt.Passed = result.Passed; attempt.ResultJson = JsonSerializer.Serialize(result);
        Audit(accountId, app.DeveloperId, id, "TechnicalTestCompleted", result.Passed ? "Passed" : "Failed");
        await SaveAsync(ct);
        return result;
    }

    private async Task<MiniAppTechnicalTest> RequireTechnicalTest(MiniApp app, CancellationToken ct)
    {
        var hash = ConfigurationHash(app);
        var recent = DateTime.UtcNow.AddDays(-1);
        return await db.MiniAppTechnicalTests.AsNoTracking().Where(t => t.MiniAppId == app.Id && t.ConfigurationHash == hash && t.Passed && t.CreatedAt >= recent)
            .OrderByDescending(t => t.CreatedAt).FirstOrDefaultAsync(ct)
            ?? throw new MiniAppException("TECHNICAL_TEST_REQUIRED", "Chạy kiểm thử kỹ thuật thành công cho cấu hình hiện tại trước khi gửi duyệt.", 422);
    }

    public async Task DecideVersionAsync(Guid actorAccountId, Guid id, MiniAppReviewDecisionInput input, CancellationToken ct)
    {
        var app = await AppQuery().SingleOrDefaultAsync(a => a.Id == id && a.DeletedAt == null, ct)
            ?? throw new MiniAppException(MiniAppErrorCodes.NotFound, "Mini App không tồn tại.", 404);
        var version = app.Versions.SingleOrDefault(v => v.Version == input.Version);
        if (version is null || app.PendingVersion != input.Version || version.ReviewState is not ("Submitted" or "UnderReview"))
            throw new MiniAppException("REVIEW_VERSION_REQUIRED", "Hồ sơ xét duyệt đã thay đổi. Tải lại phiên bản trước khi quyết định.", 409);
        if (input.Decision is not ("UnderReview" or "ChangesRequested" or "Approved" or "Rejected"))
            throw new MiniAppException("VALIDATION_ERROR", "Quyết định xét duyệt không hợp lệ.", 422);
        if (input.Decision is "ChangesRequested" or "Rejected" && (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 500))
            throw new MiniAppException("VALIDATION_ERROR", "Cần nhập lý do yêu cầu chỉnh sửa hoặc từ chối (tối đa 500 ký tự).", 422);
        if (input.Decision == "Approved")
        {
            if (app.Developer.Status != DeveloperStatus.Active || app.Status is MiniAppStatus.Suspended or MiniAppStatus.Archived)
                throw new MiniAppException(MiniAppErrorCodes.DeveloperSuspended, "Không thể duyệt khi Developer hoặc ứng dụng bị khóa.", 403);
            await RequireVerifiedDomains(app, JsonSerializer.Deserialize<MiniAppConfigurationInput>(version.ConfigurationJson)!, ct);
            ValidateSubmittedTechnicalTest(version);
        }
        var previous = version.ReviewState;
        version.ReviewState = input.Decision; version.Reason = SafeDetail(input.Reason); version.ReviewedByAccountId = actorAccountId; version.ReviewedAt = DateTime.UtcNow;
        if (input.Decision != "UnderReview")
        {
            app.PendingVersion = null;
            if (app.PublishedVersion == 0) app.Status = input.Decision is "ChangesRequested" or "Rejected" ? MiniAppStatus.Rejected : MiniAppStatus.Draft;
            version.Status = input.Decision == "Approved" ? MiniAppStatus.Draft : MiniAppStatus.Rejected;
        }
        Audit(actorAccountId, app.DeveloperId, id, "VersionReviewDecision", JsonSerializer.Serialize(new { version = input.Version, previous, next = input.Decision, reason = SafeDetail(input.Reason) }));
        await NotifyOwner(app, $"Mini App: {input.Decision}", SafeDetail(input.Reason) ?? "Trạng thái xét duyệt phiên bản đã được cập nhật.", ct);
        await SaveAsync(ct);
    }

    public async Task PublishOwnedAsync(Guid accountId, Guid id, int versionNumber, CancellationToken ct)
    {
        var app = await RequireOwned(accountId, id, ct);
        await RequireAppCapability(accountId, app, "edit", ct);
        if (app.Status is MiniAppStatus.Suspended or MiniAppStatus.Archived || app.PendingVersion != null)
            throw new MiniAppException("INVALID_STATUS_TRANSITION", "Ứng dụng đang bị khóa hoặc có phiên bản đang xét duyệt.", 409);
        var version = app.Versions.SingleOrDefault(v => v.Version == versionNumber && v.ReviewState == "Approved");
        if (version is null || versionNumber < app.PublishedVersion || versionNumber == app.PublishedVersion && app.Status != MiniAppStatus.Unpublished)
            throw new MiniAppException("APPROVED_VERSION_REQUIRED", "Chỉ được xuất bản phiên bản mới đã được phê duyệt.", 409);
        await RequireVerifiedDomains(app, JsonSerializer.Deserialize<MiniAppConfigurationInput>(version.ConfigurationJson)!, ct);
        ValidateSubmittedTechnicalTest(version);
        app.PublishedVersion = version.Version; app.PublishedConfigurationJson = version.ConfigurationJson; app.Status = MiniAppStatus.Active;
        MiniAppConfigurationSnapshot.RefreshCatalog(app);
        version.Status = MiniAppStatus.Active;
        Audit(accountId, app.DeveloperId, id, "MiniAppPublished", versionNumber.ToString());
        await NotifyOwner(app, "Mini App đã xuất bản", $"Phiên bản {versionNumber} đã được phát hành trên ANKT.", ct);
        await SaveAsync(ct);
    }

    public async Task UnpublishOwnedAsync(Guid accountId, Guid id, CancellationToken ct)
    {
        var app = await RequireOwned(accountId, id, ct);
        await RequireAppCapability(accountId, app, "manage", ct);
        if (app.Status != MiniAppStatus.Active) throw new MiniAppException("INVALID_STATUS_TRANSITION", "Chỉ có thể ngừng phát hành ứng dụng đang hoạt động.", 409);
        app.Status = MiniAppStatus.Unpublished;
        Audit(accountId, app.DeveloperId, id, "MiniAppUnpublished");
        await NotifyOwner(app, "Mini App đã ngừng phát hành", "Ứng dụng tạm ngừng nhận phiên mới; dữ liệu được giữ nguyên.", ct);
        await SaveAsync(ct);
    }

    private static void ValidateSubmittedTechnicalTest(MiniAppVersion version)
    {
        if (version.TechnicalTestJson is null || version.ConfigurationHash is null)
            throw new MiniAppException("TECHNICAL_TEST_REQUIRED", "Phiên bản này cần gửi lại với kết quả kiểm thử kỹ thuật.", 422);
        var test = JsonSerializer.Deserialize<MiniAppTechnicalTestDto>(version.TechnicalTestJson);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(version.ConfigurationJson)));
        if (test is null || !test.Passed || test.ConfigurationHash != hash || version.ConfigurationHash != hash)
            throw new MiniAppException("TECHNICAL_TEST_REQUIRED", "Kết quả kiểm thử không khớp phiên bản gửi duyệt.", 422);
    }
}
