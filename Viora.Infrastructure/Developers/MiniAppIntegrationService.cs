using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Developers;

public sealed class MiniAppIntegrationService(AppDbContext db, MiniAppSecretProtector protector, IPublicWebFetcher fetcher, IHostEnvironment environment, TimeProvider clock,
    MiniAppVersionService? versions = null)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private async Task<MiniApp> Own(Guid owner, string appId, CancellationToken ct) => await db.MiniApps
        .SingleOrDefaultAsync(x => x.AppId == appId && x.DeveloperAccountId == owner && x.DeletedAt == null, ct)
        ?? throw new DeveloperAuthException(404, "MINI_APP_NOT_FOUND", "Không tìm thấy Mini App.");
    private void Audit(MiniApp app, string action) => db.MiniAppAuditLogs.Add(new() { MiniAppId = app.Id, ActorDeveloperId = app.DeveloperAccountId, Action = action });
    private static void Touch(MiniApp app) => app.Version = Guid.NewGuid();
    private Task<MiniAppDomainVerification?> Current(MiniApp app, CancellationToken ct)
    {
        var hostname = MiniAppDomainLifecycle.Host(app.WebsiteUrl);
        return db.MiniAppDomainVerifications.SingleOrDefaultAsync(x => x.MiniAppId == app.Id && x.Hostname == hostname && x.RevokedAt == null, ct);
    }
    private static MiniAppIntegrationDto Map(MiniApp a) => new(a.WebsiteUrl ?? "", a.SupportEmail ?? "", a.ShortDescription ?? "", a.Description ?? "", a.PrivacyPolicyUrl ?? "", a.TermsOfServiceUrl ?? "");
    private static string RequireHost(MiniApp app) => MiniAppDomainLifecycle.Host(app.WebsiteUrl) ?? throw MiniAppInput.Invalid("Lưu Website URL HTTPS công khai trước khi xác minh domain.");

    public async Task<MiniAppIntegrationDto> Get(Guid owner, string appId, CancellationToken ct)
    {
        var app = await Own(owner, appId, ct); return Map(app);
    }
    public async Task<MiniAppIntegrationDto> Save(Guid owner, string appId, MiniAppIntegrationRequest r, CancellationToken ct)
    {
        // Validate the full configuration before any write; empty optional fields permit incremental drafts.
        var website = PublicWebAddress.Normalize(r.WebsiteUrl, allowEmpty: true);
        var privacy = PublicWebAddress.Normalize(r.PrivacyPolicyUrl, allowEmpty: true);
        var terms = PublicWebAddress.Normalize(r.TermsOfServiceUrl, allowEmpty: true);
        var email = string.IsNullOrWhiteSpace(r.SupportEmail) ? "" : DeveloperInput.Email(r.SupportEmail).ToLowerInvariant();
        var shortDescription = MiniAppInput.Text(r.ShortDescription, 300); var description = MiniAppInput.Text(r.Description, 10000);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var app = await Own(owner, appId, ct); var previous = app.WebsiteUrl;
        app.WebsiteUrl = website; app.PrivacyPolicyUrl = privacy; app.TermsOfServiceUrl = terms;
        app.SupportEmail = email; app.ShortDescription = shortDescription; app.Description = description;
        await MiniAppDomainLifecycle.ChangeWebsite(db, app, previous, protector, ct);
        if (app.Status is MiniAppStatus.Draft or MiniAppStatus.ConfigurationIncomplete)
            app.Status = !string.IsNullOrWhiteSpace(app.LogoUrl) && website != "" && privacy != "" && terms != "" && email != "" && shortDescription != "" ? MiniAppStatus.Draft : MiniAppStatus.ConfigurationIncomplete;
        Touch(app); Audit(app, "integration.updated"); await db.SaveChangesAsync(ct);
        if (versions != null) await versions.SyncWorkingAsync(app, ct);
        await tx.CommitAsync(ct);
        return Map(app);
    }

    private MiniAppDomainDto DomainDto(MiniApp app, MiniAppDomainVerification? row)
    {
        var host = MiniAppDomainLifecycle.Host(app.WebsiteUrl);
        if (host is null || row is null || row.Hostname != host || row.RevokedAt is not null)
            return new(host, "Pending", null, null, null, null, null, null, host is null ? "Lưu Website URL trước khi tải file xác minh." : "Tải file xác minh để bắt đầu.");
        var expired = row.ExpiresAt <= Now;
        return new(host, expired ? "Expired" : row.Status.ToString(), row.VerificationFileName, $"https://{host}/{row.VerificationFileName}",
            row.VerifiedAt, row.LastCheckedAt, row.ExpiresAt, expired ? "CHALLENGE_EXPIRED" : row.LastErrorCode,
            expired ? "File xác minh đã hết hạn. Hãy tạo lại file." : row.LastErrorCode is null ? null : ErrorMessage(row.LastErrorCode));
    }
    public async Task<MiniAppDomainDto> Domain(Guid owner, string appId, CancellationToken ct)
    {
        var app = await Own(owner, appId, ct); return DomainDto(app, await Current(app, ct));
    }
    private async Task<MiniAppDomainVerification> EnsureChallenge(MiniApp app, bool regenerate, CancellationToken ct)
    {
        var host = RequireHost(app); var row = await Current(app, ct);
        if (!regenerate && row is not null && row.Hostname == host && row.ExpiresAt > Now) return row;
        await MiniAppDomainLifecycle.Revoke(db, app.Id, ct, host);
        if (row is not null) db.Entry(row).State = EntityState.Detached;
        Touch(app); return MiniAppDomainLifecycle.Create(db, app, host, protector);
    }
    public async Task<MiniAppVerificationFile> File(Guid owner, string appId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var app = await Own(owner, appId, ct); var row = await EnsureChallenge(app, false, ct);
        var content = FileContent(app, row); Audit(app, "domain.file_downloaded");
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return new(row.VerificationFileName, content);
    }
    private string FileContent(MiniApp app, MiniAppDomainVerification row)
    {
        var token = protector.Decrypt(row.VerificationMaterialEncrypted, row.SecretKeyVersion, row.Id, MiniAppDomainLifecycle.Purpose);
        return $"ankt-domain-verification\napp_id={app.AppId}\ndomain={row.Hostname}\ntoken={token}\n";
    }
    public async Task<MiniAppDomainDto> Regenerate(Guid owner, string appId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var app = await Own(owner, appId, ct); var row = await EnsureChallenge(app, true, ct);
        Audit(app, "domain.regenerated"); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return DomainDto(app, row);
    }
    public async Task<MiniAppDomainDto> Verify(Guid owner, string appId, CancellationToken ct)
    {
        var app = await Own(owner, appId, ct); var host = RequireHost(app); var row = await Current(app, ct);
        if (row is null || row.Hostname != host || row.ExpiresAt <= Now)
            throw new DeveloperAuthException(409, "CHALLENGE_EXPIRED", "Chưa có file xác minh hợp lệ hoặc file đã hết hạn. Hãy tải hoặc tạo lại file.");
        string? error = null;
        try
        {
            // Destination is built only from persisted canonical host and system-generated filename.
            var fetched = await fetcher.FetchAsync(new Uri($"https://{host}/{row.VerificationFileName}"), 4096, true, ct);
            if (!ValidProof(app, row, fetched.Content)) error = "PROOF_MISMATCH";
        }
        catch (PublicFetchException ex) { error = ex.Code; }
        // Network I/O runs outside a DB transaction; both CAS tokens guard the result against concurrent writes.
        if (row.ExpiresAt <= Now) throw new DeveloperAuthException(409, "CHALLENGE_EXPIRED", "File xác minh đã hết hạn trong khi kiểm tra. Hãy tạo lại file.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        Touch(app); row.Version = Guid.NewGuid(); row.LastCheckedAt = Now;
        row.LastErrorCode = error; row.Status = error is null ? MiniAppDomainStatus.Verified : MiniAppDomainStatus.Failed;
        if (error is null) { row.VerifiedAt = Now; row.ExpiresAt = Now.AddDays(30); }
        Audit(app, error is null ? "domain.verified" : "domain.verification_failed");
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return DomainDto(app, row);
    }
    private static bool ValidProof(MiniApp app, MiniAppDomainVerification row, byte[] content)
    {
        string text;
        try { text = new UTF8Encoding(false, true).GetString(content); } catch (DecoderFallbackException) { return false; }
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (lines.Length != 5 || lines[0] != "ankt-domain-verification" || lines[1] != "app_id=" + app.AppId || lines[2] != "domain=" + row.Hostname || lines[4] != "" || !lines[3].StartsWith("token=", StringComparison.Ordinal)) return false;
        var token = lines[3][6..];
        if (token.Length != 43 || token.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_')) return false;
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(token)); var expected = Convert.FromHexString(row.VerificationTokenHash);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public async Task<MiniAppReadinessDto> Readiness(Guid owner, string appId, CancellationToken ct)
    {
        var app = await Own(owner, appId, ct); var row = await Current(app, ct);
        var version = app.Version; var domainVersion = row?.Version;
        var checks = new List<MiniAppReadinessCheck>();
        void Check(string key, bool pass, string message, string requirement = "required") => checks.Add(new(key, requirement, pass ? "passed" : "failed", pass ? "Đã cấu hình." : message));
        Check("logo", !string.IsNullOrWhiteSpace(app.LogoUrl), "Chưa có logo."); Check("name", !string.IsNullOrWhiteSpace(app.Name), "Chưa có tên.");
        Check("category", MiniAppInput.Categories.Contains(app.Category), "Chưa có danh mục hợp lệ.");
        Check("description", !string.IsNullOrWhiteSpace(app.ShortDescription) && !string.IsNullOrWhiteSpace(app.Description), "Bổ sung mô tả ngắn và giới thiệu chi tiết.");
        Check("website", ValidUrl(app.WebsiteUrl), "Chưa có Website URL HTTPS hợp lệ.");
        Check("domain", row is { Status: MiniAppDomainStatus.Verified, RevokedAt: null } && row.Hostname == MiniAppDomainLifecycle.Host(app.WebsiteUrl) && row.ExpiresAt > Now, "Domain chưa xác minh hoặc proof đã hết hạn.");
        Check("supportEmail", ValidEmail(app.SupportEmail), "Chưa có email hỗ trợ hợp lệ.");
        var policies = await Task.WhenAll(Policy("privacyPolicy", app.PrivacyPolicyUrl, ct), Policy("termsOfService", app.TermsOfServiceUrl, ct));
        checks.AddRange(policies);
        // Do not publish readiness for a configuration/proof snapshot changed while policy URLs were fetched.
        var current = await db.MiniApps.AsNoTracking().Where(x => x.Id == app.Id && x.DeveloperAccountId == owner && x.DeletedAt == null).Select(x => (Guid?)x.Version).SingleOrDefaultAsync(ct);
        var hostname = MiniAppDomainLifecycle.Host(app.WebsiteUrl);
        var currentDomain = await db.MiniAppDomainVerifications.AsNoTracking().Where(x => x.MiniAppId == app.Id && x.Hostname == hostname && x.RevokedAt == null).Select(x => new { x.Version, x.ExpiresAt }).SingleOrDefaultAsync(ct);
        if (current != version || currentDomain?.Version != domainVersion) throw new DeveloperAuthException(409, "CONFIGURATION_CHANGED", "Cấu hình vừa thay đổi. Hãy kiểm tra lại.");
        if (currentDomain is not null && currentDomain.ExpiresAt <= Now)
            checks[checks.FindIndex(x => x.Key == "domain")] = new("domain", "required", "failed", "Proof domain đã hết hạn. Hãy xác minh lại.");
        checks.Add(new("sandboxRuntime", "warning", "warning", "Sandbox runtime sẽ có ở giai đoạn sau."));
        checks.Add(new("adminReview", "warning", "warning", "Gửi kiểm duyệt sẽ có ở giai đoạn sau."));
        return new(checks.Where(x => x.Requirement == "required").All(x => x.Status == "passed"), false, false, false, false, Now, checks.ToArray());
    }
    private static bool ValidUrl(string? value) { try { PublicWebAddress.Normalize(value); return true; } catch (DeveloperAuthException) { return false; } }
    private static bool ValidEmail(string? value) { try { DeveloperInput.Email(value); return true; } catch (DeveloperAuthException) { return false; } }
    private bool ValidRedirect(string value) { try { MiniAppInput.Url(value, environment.IsDevelopment(), true); return true; } catch (DeveloperAuthException) { return false; } }
    private async Task<MiniAppReadinessCheck> Policy(string key, string? value, CancellationToken ct)
    {
        if (!ValidUrl(value)) return new(key, "required", "failed", "Chưa có URL chính sách HTTPS hợp lệ.");
        try { await fetcher.FetchAsync(new Uri(value!), 256 * 1024, false, ct); return new(key, "required", "passed", "Trang chính sách truy cập công khai qua HTTPS."); }
        catch (PublicFetchException ex) { return new(key, "required", "failed", ErrorMessage(ex.Code)); }
    }
    public static string ErrorMessage(string code) => code switch {
        "FILE_NOT_FOUND" => "Không tìm thấy file hoặc trang chính sách (404).",
        "PROOF_MISMATCH" => "Nội dung file không khớp App ID, domain hoặc token. Hãy tải đúng file mới nhất.",
        "TLS_ERROR" => "Chứng chỉ HTTPS không hợp lệ hoặc kết nối TLS thất bại.",
        "FETCH_TIMEOUT" => "Website phản hồi quá thời gian cho phép.",
        "DOMAIN_BLOCKED" => "Domain hoặc DNS trỏ tới địa chỉ mạng không được phép.",
        "DNS_ERROR" => "Không phân giải được DNS của domain.",
        "REDIRECT_REJECTED" => "Website chuyển hướng; hãy cung cấp URL HTTPS truy cập trực tiếp.",
        "RESPONSE_TOO_LARGE" => "Nội dung phản hồi vượt giới hạn cho phép.",
        "INVALID_CONTENT_TYPE" => "Định dạng phản hồi không hợp lệ hoặc nội dung bị nén.",
        "EMPTY_CONTENT" => "Website trả nội dung trống.",
        _ => "Không thể truy cập website công khai. Kiểm tra cấu hình và thử lại.",
    };
}
