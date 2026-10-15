using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Accounts;
using Viora.Application.MiniApps;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.MiniApps;

public sealed class DeveloperReauthenticationService(AppDbContext db, IPasswordHasher passwords, IGoogleIdentityTokenVerifier? google = null, IDeveloperOrganizationContext? organization = null) : IDeveloperReauthenticationService
{
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public async Task<DeveloperReauthResponse> VerifyAsync(Guid accountId, DeveloperReauthInput input, CancellationToken ct)
    {
        if (input.Operation is not ("RotateSecret" or "ChangeDomain" or "ManageTeam") || input.Operation == "ManageTeam" && input.MiniAppId != null || input.Operation != "ManageTeam" && input.MiniAppId == null || input.Password?.Length > 1024 || input.FirebaseToken?.Length > 10000 || (!string.IsNullOrEmpty(input.Password) && !string.IsNullOrEmpty(input.FirebaseToken)))
            throw new MiniAppException("VALIDATION_ERROR", "Thao tác xác thực lại không hợp lệ.", 422);
        var account = await db.Accounts.SingleOrDefaultAsync(a => a.Id == accountId && a.Status == AccountStatus.Active && a.DeletedAt == null, ct)
            ?? throw new MiniAppException("ACCOUNT_UNAVAILABLE", "Tài khoản không hoạt động.", 403);
        var selectedOrganization = input.DeveloperId ?? organization?.OrganizationId;
        if (input.DeveloperId != null && organization?.OrganizationId != null && input.DeveloperId != organization.OrganizationId)
            throw new MiniAppException("ORGANIZATION_REQUIRED", "Tổ chức xác thực không khớp tổ chức đang chọn.", 422);
        if (input.Operation == "ManageTeam" && selectedOrganization == null
            && await db.Developers.CountAsync(d => d.Status == DeveloperStatus.Active && (d.AccountId == accountId || db.DeveloperMemberships.Any(m => m.DeveloperId == d.Id && m.AccountId == accountId)), ct) > 1)
            throw new MiniAppException("ORGANIZATION_REQUIRED", "Chọn tổ chức trước khi quản lý thành viên.", 422);
        var developer = await db.Developers.Where(d => d.Status == DeveloperStatus.Active
            && (selectedOrganization == null || d.Id == selectedOrganization)
            && (d.AccountId == accountId || db.DeveloperMemberships.Any(m => m.DeveloperId == d.Id && m.AccountId == accountId)
                || input.MiniAppId != null && db.Set<MiniAppMembership>().Any(m => m.MiniAppId == input.MiniAppId && m.AccountId == accountId))
            && (input.MiniAppId == null || db.MiniApps.Any(a => a.Id == input.MiniAppId && a.DeveloperId == d.Id && a.DeletedAt == null))).OrderByDescending(d => d.AccountId == accountId).ThenBy(d => d.Id).FirstOrDefaultAsync(ct)
            ?? throw new MiniAppException("DEVELOPER_SUSPENDED", "Developer không hoạt động.", 403);
        if (input.MiniAppId is Guid appId && !await db.MiniApps.AnyAsync(a => a.Id == appId && a.DeveloperId == developer.Id && a.DeletedAt == null, ct))
            throw new MiniAppException("MINI_APP_NOT_FOUND", "Mini App không tồn tại.", 404);
        var cutoff = DateTime.UtcNow.AddMinutes(-10);
        if (await db.MiniAppAuditLogs.CountAsync(log => log.ActorAccountId == accountId && log.Action == "DeveloperReauthFailed" && log.CreatedAt >= cutoff, ct) >= 5)
            throw new MiniAppException("RATE_LIMITED", "Đã vượt số lần xác thực lại. Thử lại sau 10 phút.", 429);
        string? providerAuthenticationHash = null;
        bool verified;
        if (!string.IsNullOrWhiteSpace(input.FirebaseToken))
        {
            var identity = google is null ? null : await google.VerifyAsync(input.FirebaseToken, ct);
            var now = DateTimeOffset.UtcNow;
            verified = identity?.AuthenticatedAt is DateTimeOffset authenticatedAt
                && authenticatedAt >= now.AddMinutes(-5) && authenticatedAt <= now.AddSeconds(30)
                && await db.ExternalLogins.AnyAsync(l => l.AccountId == accountId && l.Provider == "google.com" && l.ProviderSubject == identity.ProviderSubject, ct);
            if (verified)
            {
                if (string.Equals(account.Email, identity!.Email, StringComparison.OrdinalIgnoreCase)) account.EmailVerifiedAt ??= DateTime.UtcNow;
                if (account.EmailVerifiedAt == null) throw new MiniAppException("EMAIL_VERIFICATION_REQUIRED", "Xác minh email ANKT trước khi thao tác nhạy cảm.", 403);
                providerAuthenticationHash = Hash($"google.com:{identity!.ProviderSubject}:{identity.AuthenticatedAt!.Value.ToUnixTimeSeconds()}");
                if (await db.DeveloperReauths.AnyAsync(p => p.ProviderAuthenticationHash == providerAuthenticationHash, ct))
                    throw new MiniAppException("REAUTH_REQUIRED", "Phiên xác thực Google này đã dùng. Hãy xác thực lại với Google.", 403);
            }
        }
        else
        {
            if (account.PasswordHash is null) throw new MiniAppException("REAUTH_UNAVAILABLE", "Tài khoản này dùng Google. Hãy xác thực lại bằng Google; không cần tạo mật khẩu Developer.", 403);
            if (account.EmailVerifiedAt == null) throw new MiniAppException("EMAIL_VERIFICATION_REQUIRED", "Xác minh email ANKT trước khi thao tác nhạy cảm. Mở mục xác minh email trong Developer Portal.", 403);
            verified = !string.IsNullOrEmpty(input.Password) && passwords.Verify(input.Password, account.PasswordHash);
        }
        if (!verified)
        {
            db.MiniAppAuditLogs.Add(new() { ActorAccountId = accountId, DeveloperId = developer.Id, Action = "DeveloperReauthFailed" });
            await db.SaveChangesAsync(ct);
            throw new MiniAppException("REAUTH_FAILED", "Không xác thực được tài khoản. Hãy dùng mật khẩu ANKT hoặc phiên Google mới của đúng tài khoản.", 403);
        }
        var proof = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var expires = DateTime.UtcNow.AddMinutes(5);
        db.DeveloperReauths.Add(new() { AccountId = accountId, MiniAppId = input.MiniAppId, DeveloperId = developer.Id, Operation = input.Operation, ProofHash = Hash(proof), ExpiresAt = expires, ProviderAuthenticationHash = providerAuthenticationHash });
        db.MiniAppAuditLogs.Add(new() { ActorAccountId = accountId, DeveloperId = developer.Id, MiniAppId = input.MiniAppId, Action = "DeveloperReauthVerified", Detail = input.Operation });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) when (providerAuthenticationHash != null)
        {
            throw new MiniAppException("REAUTH_REQUIRED", "Không thể cấp xác nhận Google. Hãy xác thực lại.", 403);
        }
        return new(proof, expires);
    }
    public async Task ConsumeAsync(Guid accountId, string operation, Guid? miniAppId, string? proof, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(proof) || proof.Length != 43) throw new MiniAppException("REAUTH_REQUIRED", "Xác thực lại trước khi thao tác nhạy cảm.", 403);
        var hash = Hash(proof); var now = DateTime.UtcNow;
        var selectedOrganization = organization?.OrganizationId;
        if (operation == "ManageTeam" && selectedOrganization == null)
        {
            var eligible = await db.Developers.Where(d => d.Status == DeveloperStatus.Active && (d.AccountId == accountId || db.DeveloperMemberships.Any(m => m.DeveloperId == d.Id && m.AccountId == accountId))).Select(d => d.Id).Take(2).ToListAsync(ct);
            if (eligible.Count != 1) throw new MiniAppException("ORGANIZATION_REQUIRED", "Chọn tổ chức trước khi quản lý thành viên.", 422);
            selectedOrganization = eligible[0];
        }
        var consumed = await db.DeveloperReauths.Where(p => p.ProofHash == hash && p.AccountId == accountId && p.Operation == operation && p.MiniAppId == miniAppId && p.ConsumedAt == null && p.ExpiresAt > now && (selectedOrganization == null || p.DeveloperId == selectedOrganization))
            .ExecuteUpdateAsync(update => update.SetProperty(p => p.ConsumedAt, now), ct);
        if (consumed != 1) throw new MiniAppException("REAUTH_REQUIRED", "Xác nhận đã hết hạn, đã dùng hoặc không đúng thao tác.", 403);
    }
}
