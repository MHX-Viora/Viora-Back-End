using Microsoft.EntityFrameworkCore;
using Viora.Application.Accounts;
using Viora.Application.MiniApps;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.MiniApps;

public sealed class DeveloperAccountVerificationService(AppDbContext db, IFirebaseIdentityTokenVerifier firebase) : IDeveloperAccountVerificationService
{
    private Task<Account?> AccountAsync(Guid id, CancellationToken ct) => db.Accounts.SingleOrDefaultAsync(a => a.Id == id && a.Status == AccountStatus.Active && a.DeletedAt == null, ct);
    public async Task<DeveloperAccountVerificationDto> GetAsync(Guid accountId, CancellationToken ct)
    {
        var account = await AccountAsync(accountId, ct) ?? throw new MiniAppException("ACCOUNT_UNAVAILABLE", "Tài khoản không hoạt động.", 403);
        return new(account.Email, account.EmailVerifiedAt);
    }
    public async Task<DeveloperAccountVerificationDto> VerifyEmailAsync(Guid accountId, string firebaseToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(firebaseToken) || firebaseToken.Length > 10000) throw new MiniAppException("VALIDATION_ERROR", "Mã xác minh không hợp lệ.", 422);
        var account = await AccountAsync(accountId, ct) ?? throw new MiniAppException("ACCOUNT_UNAVAILABLE", "Tài khoản không hoạt động.", 403);
        var identity = await firebase.VerifyAsync(firebaseToken, ct);
        var now = DateTimeOffset.UtcNow;
        if (string.IsNullOrWhiteSpace(identity?.Email) || !string.Equals(identity.Email, account.Email, StringComparison.OrdinalIgnoreCase)
            || identity.AuthenticatedAt is not DateTimeOffset authenticatedAt || authenticatedAt < now.AddMinutes(-5) || authenticatedAt > now.AddSeconds(30))
            throw new MiniAppException("EMAIL_VERIFICATION_FAILED", "Xác nhận mới phải thuộc đúng email tài khoản ANKT.", 403);
        account.EmailVerifiedAt ??= now.UtcDateTime;
        db.MiniAppAuditLogs.Add(new() { ActorAccountId = accountId, Action = "AccountEmailVerified" });
        await db.SaveChangesAsync(ct);
        return new(account.Email, account.EmailVerifiedAt);
    }
}
