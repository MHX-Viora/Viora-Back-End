using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Viora.Application.Developers;
using Viora.Domain.Developers;

namespace Viora.Infrastructure.Developers;

public sealed partial class DeveloperIdentityService
{
    public async Task SendEmailActionAsync(string address, bool reset, DeveloperRequestContext context, CancellationToken ct)
    {
        var normalized = DeveloperInput.Email(address); RequireEmail();
        var account = await db.DeveloperAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.EmailNormalized == normalized, ct);
        if (account is null || account.Status == DeveloperStatus.Suspended ||
            (reset && account.PasswordHash is null) || (!reset && account.EmailVerifiedAt is not null)) return;
        await SendTokenAsync(account, reset ? DeveloperTokenPurpose.ResetPassword : DeveloperTokenPurpose.VerifyEmail, context, ct);
    }

    private async Task SendTokenAsync(DeveloperAccount account, DeveloperTokenPurpose purpose, DeveloperRequestContext context, CancellationToken ct)
    {
        var now = Now;
        // Serializable protects per-account cooldown against concurrent resends.
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        if (await db.DeveloperActionTokens.AnyAsync(t => t.DeveloperAccountId == account.Id && t.Purpose == purpose && t.CreatedAt > now.AddMinutes(-1), ct)) return;
        await db.DeveloperActionTokens.Where(t => t.DeveloperAccountId == account.Id && t.Purpose == purpose && t.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, now), ct);
        var raw = NewToken();
        db.DeveloperActionTokens.Add(new() { DeveloperAccountId = account.Id, TokenHash = Hash(raw), Purpose = purpose,
            SecurityVersion = account.SecurityVersion, ExpiresAt = now.AddMinutes(purpose == DeveloperTokenPurpose.VerifyEmail ? 1440 : 30) });
        Audit(account.Id, purpose == DeveloperTokenPurpose.VerifyEmail ? "VerificationRequested" : "PasswordResetRequested", context);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        var path = purpose == DeveloperTokenPurpose.VerifyEmail ? "verify-email" : "reset-password";
        var link = $"{options.Value.PortalUrl.TrimEnd('/')}/{path}#token={raw}";
        try
        {
            var content = purpose == DeveloperTokenPurpose.VerifyEmail
                ? DeveloperEmailTemplates.Verification(account.FullName, link, now.Year)
                : DeveloperEmailTemplates.PasswordReset(account.FullName, link, now.Year);
            await email.SendAsync(account.EmailNormalized.ToLowerInvariant(), purpose == DeveloperTokenPurpose.VerifyEmail ? "Xác minh email ANKT Developer" : "Đặt lại mật khẩu ANKT Developer",
                content, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Do not log exception text: SMTP diagnostics can contain credentials or recipient PII.
            logger.LogWarning("Developer email delivery failed; category {Category}", ex.GetType().Name);
            Audit(account.Id, "EmailDeliveryFailed", context); await db.SaveChangesAsync(ct);
        }
    }

    private async Task<DeveloperActionToken> ConsumeTokenAsync(string raw, DeveloperTokenPurpose purpose, CancellationToken ct)
    {
        if (raw is null || raw.Length != 43) throw InvalidToken();
        var hash = Hash(raw); var now = Now;
        var token = await db.DeveloperActionTokens.AsNoTracking().Include(t => t.Account)
            .SingleOrDefaultAsync(t => t.TokenHash == hash && t.Purpose == purpose, ct);
        if (token is null || token.Account.Status == DeveloperStatus.Suspended || token.Account.SecurityVersion != token.SecurityVersion) throw InvalidToken();
        var claimed = await db.DeveloperActionTokens.Where(t => t.Id == token.Id && t.ConsumedAt == null && t.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, now), ct);
        if (claimed != 1) throw InvalidToken();
        return token;
    }

    public async Task VerifyEmailAsync(string raw, DeveloperRequestContext context, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var token = await ConsumeTokenAsync(raw, DeveloperTokenPurpose.VerifyEmail, ct); var now = Now;
        var changed = await db.DeveloperAccounts.Where(a => a.Id == token.DeveloperAccountId && a.SecurityVersion == token.SecurityVersion && a.Status == DeveloperStatus.PendingVerification)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.EmailVerifiedAt, now).SetProperty(a => a.Status, DeveloperStatus.Active).SetProperty(a => a.UpdatedAt, now), ct);
        if (changed != 1) throw InvalidToken();
        Audit(token.DeveloperAccountId, "EmailVerified", context); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }

    public async Task ResetPasswordAsync(DeveloperResetRequest request, DeveloperRequestContext context, CancellationToken ct)
    {
        DeveloperInput.Password(request.Password, request.ConfirmPassword);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var token = await ConsumeTokenAsync(request.Token, DeveloperTokenPurpose.ResetPassword, ct);
        await ReplacePasswordAsync(token.Account, request.Password, context, "PasswordReset", ct);
        await tx.CommitAsync(ct);
    }

    public async Task ChangePasswordAsync(Guid accountId, DeveloperChangePasswordRequest request, DeveloperRequestContext context, CancellationToken ct)
    {
        DeveloperInput.Password(request.Password, request.ConfirmPassword);
        var account = await db.DeveloperAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == accountId && a.Status == DeveloperStatus.Active && a.EmailVerifiedAt != null, ct);
        if (account is null) throw InvalidCredentials();
        await RequireCurrentPasswordAsync(account, request.CurrentPassword, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await ReplacePasswordAsync(account, request.Password, context, "PasswordChanged", ct, requireUnlocked: true); await tx.CommitAsync(ct);
    }

    private async Task ReplacePasswordAsync(DeveloperAccount account, string password, DeveloperRequestContext context, string action, CancellationToken ct, bool requireUnlocked = false)
    {
        var hash = Hasher.HashPassword(account, password); var now = Now; var securityVersion = Guid.NewGuid();
        var changed = await db.DeveloperAccounts.Where(a => a.Id == account.Id && a.SecurityVersion == account.SecurityVersion && a.Status != DeveloperStatus.Suspended &&
                (!requireUnlocked || a.LockedUntil == null || a.LockedUntil <= now))
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.PasswordHash, hash).SetProperty(a => a.SecurityVersion, securityVersion)
                .SetProperty(a => a.FailedLoginAttempts, 0).SetProperty(a => a.LockedUntil, (DateTime?)null).SetProperty(a => a.UpdatedAt, now), ct);
        if (changed != 1) throw InvalidToken();
        await db.DeveloperSessions.Where(s => s.DeveloperAccountId == account.Id && s.RevokedAt == null).ExecuteUpdateAsync(s => s.SetProperty(s => s.RevokedAt, now), ct);
        await db.DeveloperActionTokens.Where(t => t.DeveloperAccountId == account.Id && t.ConsumedAt == null).ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, now), ct);
        Audit(account.Id, action, context); await db.SaveChangesAsync(ct);
    }
}
