using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Developers;

public sealed partial class DeveloperIdentityService(
    AppDbContext db, IDeveloperEmailSender email, IOptions<DeveloperAuthOptions> options,
    TimeProvider clock, ILogger<DeveloperIdentityService> logger) : IDeveloperIdentityService
{
    private static readonly PasswordHasher<DeveloperAccount> Hasher = new(Options.Create(new PasswordHasherOptions { IterationCount = 210000 }));
    private static readonly string DummyHash = Hasher.HashPassword(new(), "Timing-only dummy credential, never an account");
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static string NewToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static DeveloperAuthException InvalidCredentials() => new(401, "INVALID_CREDENTIALS", "Không thể đăng nhập. Kiểm tra thông tin, xác minh email hoặc thử lại sau.");
    private static DeveloperAuthException InvalidToken() => new(400, "INVALID_TOKEN", "Liên kết không hợp lệ, đã hết hạn hoặc đã được sử dụng.");
    private static DeveloperAccountDto Map(DeveloperAccount a) => new(a.Id, a.FullName, a.EmailNormalized.ToLowerInvariant(), a.EmailVerifiedAt, a.AvatarUrl, a.Status.ToString(), a.PasswordHash is not null);
    private void Audit(Guid? account, string action, DeveloperRequestContext context) => db.DeveloperAuditLogs.Add(new()
    {
        DeveloperAccountId = account, Action = action,
        IpAddress = Clean(context.IpAddress, 64), UserAgent = Clean(context.UserAgent, 256),
    });
    private static string Clean(string value, int max) => new(value.Where(c => !char.IsControl(c)).Take(max).ToArray());
    private void RequireEmail()
    {
        if (!email.IsConfigured || !Uri.TryCreate(options.Value.PortalUrl, UriKind.Absolute, out var url) ||
            url.Scheme is not ("https" or "http") || url.AbsolutePath.TrimEnd('/') != "/developers" ||
            url.Query.Length != 0 || url.Fragment.Length != 0 || !string.IsNullOrEmpty(url.UserInfo) ||
            (url.Scheme == "http" && !url.IsLoopback))
            throw new DeveloperAuthException(503, "EMAIL_UNAVAILABLE", "Dịch vụ email chưa sẵn sàng. Vui lòng thử lại sau.");
    }

    public async Task RegisterAsync(DeveloperRegisterRequest request, DeveloperRequestContext context, CancellationToken ct)
    {
        var normalized = DeveloperInput.Email(request.Email); DeveloperInput.Password(request.Password, request.ConfirmPassword);
        var name = request.FullName?.Trim() ?? "";
        if (name.Length is < 2 or > 120 || !request.AcceptTerms)
            throw new DeveloperAuthException(400, "VALIDATION_ERROR", "Nhập họ tên hợp lệ và đồng ý điều khoản.");
        RequireEmail();
        var account = new DeveloperAccount { FullName = name, EmailNormalized = normalized, TermsAcceptedAt = Now };
        account.PasswordHash = Hasher.HashPassword(account, request.Password);
        if (await db.DeveloperAccounts.AnyAsync(a => a.EmailNormalized == normalized, ct)) return;
        db.DeveloperAccounts.Add(account); Audit(account.Id, "Registered", context);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            if (await db.DeveloperAccounts.AnyAsync(a => a.EmailNormalized == normalized, ct)) return;
            throw;
        }
        await SendTokenAsync(account, DeveloperTokenPurpose.VerifyEmail, context, ct);
    }

    public async Task<DeveloperLoginResult> LoginAsync(DeveloperLoginRequest request, DeveloperRequestContext context, CancellationToken ct)
    {
        var normalized = DeveloperInput.Email(request.Email);
        if (request.Password is null || request.Password.Length > 128) throw InvalidCredentials();
        var account = await db.DeveloperAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.EmailNormalized == normalized, ct);
        var valid = Hasher.VerifyHashedPassword(account ?? new(), account?.PasswordHash ?? DummyHash, request.Password) != PasswordVerificationResult.Failed;
        var now = Now;
        if (account is null || !valid || account.PasswordHash is null || account.Status != DeveloperStatus.Active || account.EmailVerifiedAt is null || account.LockedUntil > now)
        {
            if (account is not null && !valid)
                await RecordPasswordFailureAsync(account.Id, ct);
            Audit(account?.Id, "LoginFailed", context); await db.SaveChangesAsync(ct); throw InvalidCredentials();
        }
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var updated = await db.DeveloperAccounts.Where(a => a.Id == account.Id && a.PasswordHash == account.PasswordHash &&
            a.SecurityVersion == account.SecurityVersion && a.Status == DeveloperStatus.Active && (a.LockedUntil == null || a.LockedUntil <= now))
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.FailedLoginAttempts, 0).SetProperty(a => a.LockedUntil, (DateTime?)null).SetProperty(a => a.LastLoginAt, now), ct);
        if (updated != 1) throw InvalidCredentials();
        var raw = NewToken();
        var session = new DeveloperSession { DeveloperAccountId = account.Id, TokenHash = Hash(raw), LastUsedAt = now,
            ExpiresAt = now.AddHours(8), AbsoluteExpiresAt = request.RememberMe ? now.AddDays(30) : now.AddHours(8), DeviceInfo = Clean(context.UserAgent, 256) };
        db.DeveloperSessions.Add(session); Audit(account.Id, "LoggedIn", context);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return new(Map(account), raw, session.ExpiresAt, session.AbsoluteExpiresAt, request.RememberMe);
    }

    private Task<int> RecordPasswordFailureAsync(Guid accountId, CancellationToken ct)
    {
        var now = Now;
        return db.DeveloperAccounts.Where(a => a.Id == accountId).ExecuteUpdateAsync(s => s
            .SetProperty(a => a.FailedLoginAttempts, a => a.FailedLoginAttempts + 1)
            .SetProperty(a => a.LockedUntil, a => a.FailedLoginAttempts >= 4 ? now.AddMinutes(15) : a.LockedUntil), ct);
    }

    private async Task RequireCurrentPasswordAsync(DeveloperAccount account, string? password, CancellationToken ct)
    {
        if (account.LockedUntil > Now) throw InvalidCredentials();
        if (account.PasswordHash is null || password is null || password.Length > 128 ||
            Hasher.VerifyHashedPassword(account, account.PasswordHash, password) == PasswordVerificationResult.Failed)
        {
            await RecordPasswordFailureAsync(account.Id, ct);
            throw InvalidCredentials();
        }
    }

    public async Task<DeveloperAccountDto> UpdateProfileAsync(Guid accountId, DeveloperProfileRequest request, DeveloperRequestContext context, CancellationToken ct)
    {
        var name = request.FullName?.Trim() ?? "";
        var avatar = string.IsNullOrWhiteSpace(request.AvatarUrl) ? null : request.AvatarUrl.Trim();
        if (name.Length is < 2 or > 120 || (avatar is not null && (avatar.Length > 2048 || !Uri.TryCreate(avatar, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0)))
            throw new DeveloperAuthException(400, "VALIDATION_ERROR", "Họ tên hoặc URL ảnh HTTPS không hợp lệ.");
        var changed = await db.DeveloperAccounts.Where(a => a.Id == accountId && a.Status == DeveloperStatus.Active && a.EmailVerifiedAt != null)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.FullName, name).SetProperty(a => a.AvatarUrl, avatar).SetProperty(a => a.UpdatedAt, Now), ct);
        if (changed != 1) throw InvalidCredentials();
        Audit(accountId, "ProfileUpdated", context); await db.SaveChangesAsync(ct);
        return Map(await db.DeveloperAccounts.AsNoTracking().SingleAsync(a => a.Id == accountId, ct));
    }
}
