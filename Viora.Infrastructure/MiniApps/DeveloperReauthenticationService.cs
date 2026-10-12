using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Accounts;
using Viora.Application.MiniApps;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.MiniApps;

public sealed class DeveloperReauthenticationService(AppDbContext db, IPasswordHasher passwords) : IDeveloperReauthenticationService
{
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public async Task<DeveloperReauthResponse> VerifyAsync(Guid accountId, DeveloperReauthInput input, CancellationToken ct)
    {
        if (input.Operation is not ("RotateSecret" or "ChangeDomain" or "ManageTeam") || input.Operation == "ManageTeam" && input.MiniAppId != null || input.Operation != "ManageTeam" && input.MiniAppId == null || input.Password is null || input.Password.Length > 1024)
            throw new MiniAppException("VALIDATION_ERROR", "Thao tác xác thực lại không hợp lệ.", 422);
        var account = await db.Accounts.SingleOrDefaultAsync(a => a.Id == accountId && a.Status == AccountStatus.Active && a.DeletedAt == null, ct)
            ?? throw new MiniAppException("ACCOUNT_UNAVAILABLE", "Tài khoản không hoạt động.", 403);
        var developer = await db.Developers.SingleOrDefaultAsync(d => d.Status == DeveloperStatus.Active && (d.AccountId == accountId || db.DeveloperMemberships.Any(m => m.DeveloperId == d.Id && m.AccountId == accountId)), ct)
            ?? throw new MiniAppException("DEVELOPER_SUSPENDED", "Developer không hoạt động.", 403);
        if (input.MiniAppId is Guid appId && !await db.MiniApps.AnyAsync(a => a.Id == appId && a.DeveloperId == developer.Id && a.DeletedAt == null, ct))
            throw new MiniAppException("MINI_APP_NOT_FOUND", "Mini App không tồn tại.", 404);
        if (account.PasswordHash is null) throw new MiniAppException("REAUTH_UNAVAILABLE", "Tài khoản này chưa có mật khẩu; cần phương thức xác thực lại được ANKT hỗ trợ trước khi thao tác nhạy cảm.", 403);
        var cutoff = DateTime.UtcNow.AddMinutes(-10);
        if (await db.MiniAppAuditLogs.CountAsync(log => log.ActorAccountId == accountId && log.Action == "DeveloperReauthFailed" && log.CreatedAt >= cutoff, ct) >= 5)
            throw new MiniAppException("RATE_LIMITED", "Đã vượt số lần xác thực lại. Thử lại sau 10 phút.", 429);
        if (!passwords.Verify(input.Password, account.PasswordHash))
        {
            db.MiniAppAuditLogs.Add(new() { ActorAccountId = accountId, DeveloperId = developer.Id, Action = "DeveloperReauthFailed" });
            await db.SaveChangesAsync(ct);
            throw new MiniAppException("REAUTH_FAILED", "Mật khẩu không đúng.", 403);
        }
        var proof = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var expires = DateTime.UtcNow.AddMinutes(5);
        db.DeveloperReauths.Add(new() { AccountId = accountId, MiniAppId = input.MiniAppId, Operation = input.Operation, ProofHash = Hash(proof), ExpiresAt = expires });
        db.MiniAppAuditLogs.Add(new() { ActorAccountId = accountId, DeveloperId = developer.Id, MiniAppId = input.MiniAppId, Action = "DeveloperReauthVerified", Detail = input.Operation });
        await db.SaveChangesAsync(ct);
        return new(proof, expires);
    }
    public async Task ConsumeAsync(Guid accountId, string operation, Guid? miniAppId, string? proof, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(proof) || proof.Length != 43) throw new MiniAppException("REAUTH_REQUIRED", "Xác thực lại trước khi thao tác nhạy cảm.", 403);
        var hash = Hash(proof); var now = DateTime.UtcNow;
        var consumed = await db.DeveloperReauths.Where(p => p.ProofHash == hash && p.AccountId == accountId && p.Operation == operation && p.MiniAppId == miniAppId && p.ConsumedAt == null && p.ExpiresAt > now)
            .ExecuteUpdateAsync(update => update.SetProperty(p => p.ConsumedAt, now), ct);
        if (consumed != 1) throw new MiniAppException("REAUTH_REQUIRED", "Xác nhận đã hết hạn, đã dùng hoặc không đúng thao tác.", 403);
    }
}
