using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Viora.Application.MiniApps;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.MiniApps;

public sealed partial class MiniAppManagementService
{
    // The fallback supports direct construction in tests. Production uses DI's persisted key ring.
    private static readonly IDataProtectionProvider FallbackChallengeProtection = new EphemeralDataProtectionProvider();
    private IDataProtector DomainProtector(MiniAppVerifiedDomain domain) => (dataProtection ?? FallbackChallengeProtection)
        .CreateProtector("ANKT.DomainChallenge.v1", domain.MiniAppId.ToString(), domain.Id.ToString(), domain.Host);
    private MiniAppDomainDto DomainDto(MiniAppVerifiedDomain domain)
    {
        var token = domain.ChallengeToken;
        var fileName = domain.VerificationFileName ?? "ankt-mini-app-verification.txt";
        string? content = token;
        if (domain.VerificationFileName is not null)
        {
            token = ""; content = null;
            if (domain.ExpiresAt > DateTime.UtcNow && domain.ProtectedChallengeToken is not null)
            {
                try { token = DomainProtector(domain).Unprotect(domain.ProtectedChallengeToken); content = "ankt-domain-verification=" + token; }
                catch (CryptographicException) { /* Key unavailable: regenerate challenge, never expose protected payload. */ }
            }
        }
        return new(domain.Id, domain.Host, domain.VerifiedAt, token, fileName, $"https://{domain.Host}/{fileName}", domain.ExpiresAt,
            content, domain.LastAttemptAt, domain.LastError);
    }
    private void NewDomainChallenge(MiniAppVerifiedDomain domain)
    {
        var token = credentials.CreateLaunchCode();
        domain.VerificationFileName = "ankt-verify-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant() + ".txt";
        domain.ChallengeToken = credentials.HashLaunchCode(token);
        domain.ProtectedChallengeToken = DomainProtector(domain).Protect(token);
        domain.ExpiresAt = DateTime.UtcNow.AddHours(48);
        domain.LastAttemptAt = null; domain.LastError = null;
    }
    private async Task PrepareDomainChallenges(MiniApp app, CancellationToken ct)
    {
        static string Host(string url) => Uri.TryCreate(url, UriKind.Absolute, out var parsed) ? parsed.IdnHost : "";
        var hosts = app.AllowedDomains.Concat([Host(app.WebUrl)]).Concat(app.CallbackUrls.Select(Host)).Concat(app.AllowedOrigins.Select(Host))
            .Where(host => !string.IsNullOrEmpty(host)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var existing = await db.MiniAppVerifiedDomains.Where(d => d.MiniAppId == app.Id).Select(d => d.Host).ToArrayAsync(ct);
        if (existing.Concat(hosts).Distinct().Count() > 32) throw new MiniAppException("DOMAIN_LIMIT", "Mỗi ứng dụng được khai báo tối đa 32 domain.", 422);
        foreach (var host in hosts.Except(existing, StringComparer.OrdinalIgnoreCase))
        {
            var domain = new MiniAppVerifiedDomain { MiniAppId = app.Id, Host = host };
            NewDomainChallenge(domain); db.MiniAppVerifiedDomains.Add(domain);
        }
    }
    public async Task<IReadOnlyList<MiniAppDomainDto>> GetDomainsAsync(Guid accountId, Guid id, CancellationToken ct)
    {
        await RequireOwned(accountId, id, ct);
        return (await db.MiniAppVerifiedDomains.AsNoTracking().Where(d => d.MiniAppId == id).OrderBy(d => d.Host).ToListAsync(ct)).Select(DomainDto).ToArray();
    }
    public async Task<MiniAppDomainDto> AddDomainAsync(Guid accountId, Guid id, MiniAppDomainInput input, CancellationToken ct)
    {
        var app = await RequireOwned(accountId, id, ct);
        await RequireAppCapability(accountId, app, "edit", ct);
        var hosts = MiniAppSecurityPolicy.NormalizeDomains([input.Host]);
        if (hosts.Length != 1 || !MiniAppSecurityPolicy.IsPublicHost(hosts[0]) || hosts[0].Contains('*'))
            throw new MiniAppException("VALIDATION_ERROR", "Cần hostname công khai chính xác.", 422);
        var domain = await db.MiniAppVerifiedDomains.SingleOrDefaultAsync(d => d.MiniAppId == id && d.Host == hosts[0], ct);
        if (domain is null)
        {
            if (await db.MiniAppVerifiedDomains.CountAsync(d => d.MiniAppId == id, ct) >= 32)
                throw new MiniAppException("DOMAIN_LIMIT", "Mỗi ứng dụng được khai báo tối đa 32 domain.", 422);
            domain = new MiniAppVerifiedDomain { MiniAppId = id, Host = hosts[0] };
            NewDomainChallenge(domain);
            db.MiniAppVerifiedDomains.Add(domain); Audit(accountId, app.DeveloperId, id, "DomainChallengeCreated"); await SaveAsync(ct);
        }
        return DomainDto(domain);
    }
    public async Task<MiniAppDomainDto> RenewDomainAsync(Guid accountId, Guid id, Guid domainId, CancellationToken ct)
    {
        var app = await RequireOwned(accountId, id, ct);
        await RequireAppCapability(accountId, app, "edit", ct);
        var domain = await db.MiniAppVerifiedDomains.SingleOrDefaultAsync(d => d.Id == domainId && d.MiniAppId == id, ct)
            ?? throw new MiniAppException("DOMAIN_NOT_FOUND", "Domain không tồn tại.", 404);
        if (domain.VerifiedAt is not null) throw new MiniAppException("DOMAIN_ALREADY_VERIFIED", "Domain đã có bằng chứng xác minh hợp lệ.", 409);
        var now = DateTime.UtcNow;
        if (await db.MiniAppAuditLogs.CountAsync(log => log.MiniAppId == id && log.Action == "DomainChallengeRenewed" && log.CreatedAt >= now.AddMinutes(-10), ct) >= 10)
            throw new MiniAppException("RATE_LIMITED", "Đã vượt số lần tạo lại mã. Thử lại sau 10 phút.", 429);
        NewDomainChallenge(domain); Audit(accountId, app.DeveloperId, id, "DomainChallengeRenewed"); await SaveAsync(ct);
        return DomainDto(domain);
    }
    public async Task<MiniAppDomainDto> VerifyDomainAsync(Guid accountId, Guid id, Guid domainId, CancellationToken ct)
    {
        var app = await RequireOwned(accountId, id, ct);
        await RequireAppCapability(accountId, app, "edit", ct);
        return await VerifyDomain(accountId, app, domainId, ct);
    }
    private async Task<MiniAppDomainDto> VerifyDomain(Guid actorAccountId, MiniApp app, Guid domainId, CancellationToken ct, bool force = false)
    {
        var domain = await db.MiniAppVerifiedDomains.SingleOrDefaultAsync(d => d.Id == domainId && d.MiniAppId == app.Id, ct)
            ?? throw new MiniAppException("DOMAIN_NOT_FOUND", "Domain không tồn tại.", 404);
        if (domain.VerifiedAt is not null && !force) return DomainDto(domain);
        var now = DateTime.UtcNow;
        if (domain.VerifiedAt is null && domain.ExpiresAt <= now)
        {
            domain.LastError = "Mã xác minh đã hết hạn. Tạo file xác minh mới."; await SaveAsync(ct);
            throw new MiniAppException("DOMAIN_CHALLENGE_EXPIRED", domain.LastError, 422);
        }
        var attempted = await db.MiniAppVerifiedDomains.Where(d => d.Id == domain.Id && (d.LastAttemptAt == null || d.LastAttemptAt < now.AddSeconds(-30)))
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.LastAttemptAt, now), ct);
        if (attempted != 1) throw new MiniAppException("RATE_LIMITED", "Chờ 30 giây trước khi thử xác minh lại.", 429);
        domain.LastAttemptAt = now;
        var dto = DomainDto(domain);
        var content = dto.FileContent;
        if (force && domain.VerifiedAt is not null && content is null && domain.ProtectedChallengeToken is not null)
        {
            try { content = "ankt-domain-verification=" + DomainProtector(domain).Unprotect(domain.ProtectedChallengeToken); }
            catch (CryptographicException) { /* Missing key cannot establish ownership. */ }
        }
        var result = content is null ? new MiniAppDomainCheck(false, "DOMAIN_CHALLENGE_UNAVAILABLE", "Không thể tải mã hiện tại. Tạo file xác minh mới.")
            : domainVerifier is null ? new(false, "DOMAIN_VERIFICATION_UNAVAILABLE", "Dịch vụ xác minh chưa sẵn sàng.")
            : await domainVerifier.CheckAsync(domain.Host, dto.VerificationFileName!, content, ct);
        domain.LastError = result.Verified ? null : result.Message;
        domain.VerifiedAt = result.Verified ? now : null;
        Audit(actorAccountId, app.DeveloperId, app.Id, result.Verified ? "DomainVerified" : "DomainVerificationFailed", result.Code);
        await SaveAsync(ct);
        if (!result.Verified) throw new MiniAppException(result.Code ?? "DOMAIN_VERIFICATION_FAILED", result.Message ?? "Xác minh chưa đạt.", 422);
        return DomainDto(domain);
    }
}
