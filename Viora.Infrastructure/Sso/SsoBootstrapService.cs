using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Sso;

public sealed record SsoBootstrapStart(Guid TransactionId, string BrowserCredential, DateTime ExpiresAt);
public sealed record SsoBootstrapComplete(IssuedSsoSession Session, string ReturnUrl);

public sealed class SsoBootstrapService(AppDbContext db, SsoSessionService sessions, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static string Hash(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    private static DeveloperAuthException Invalid() => new(400, "SSO_TRANSACTION_INVALID", "Phiên đăng nhập đã hết hạn hoặc không thuộc trình duyệt này. Hãy thử lại.");

    public async Task<SsoBootstrapStart> StartAsync(string returnUrl, CancellationToken ct)
    {
        if (returnUrl.Length > 8192 || !(returnUrl == "/connect/authorize" || returnUrl.StartsWith("/connect/authorize?", StringComparison.Ordinal)) ||
            returnUrl.Any(x => char.IsControl(x) || x == '\\')) throw Invalid();
        var browser = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var transaction = new SsoLoginTransaction { BrowserHash = Hash(browser), ReturnUrl = returnUrl, ExpiresAt = Now.AddMinutes(2) };
        db.SsoLoginTransactions.Add(transaction); await db.SaveChangesAsync(ct);
        return new(transaction.Id, browser, transaction.ExpiresAt);
    }

    public async Task AuthenticateAsync(Guid transactionId, Guid accountId, Guid signedSocialSessionId, CancellationToken ct)
    {
        await sessions.ValidateSocialSessionAsync(accountId, signedSocialSessionId, ct);
        var now = Now;
        var count = await db.SsoLoginTransactions.Where(x => x.Id == transactionId && x.ConsumedAt == null && x.ExpiresAt > now && x.AccountId == null)
            .ExecuteUpdateAsync(x => x.SetProperty(t => t.AccountId, accountId).SetProperty(t => t.SocialSessionId, signedSocialSessionId), ct);
        if (count != 1) throw Invalid();
    }

    public async Task<SsoBootstrapComplete> CompleteAsync(Guid transactionId, string? browserCredential, CancellationToken ct)
    {
        if (browserCredential is not { Length: 43 }) throw Invalid();
        var now = Now;
        var transaction = await db.SsoLoginTransactions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == transactionId && x.ExpiresAt > now && x.ConsumedAt == null, ct);
        if (transaction?.AccountId == null || transaction.SocialSessionId == null ||
            !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(transaction.BrowserHash), Convert.FromHexString(Hash(browserCredential)))) throw Invalid();
        await using var atomic = await db.Database.BeginTransactionAsync(ct);
        var claimed = await db.SsoLoginTransactions.Where(x => x.Id == transactionId && x.ConsumedAt == null && x.ExpiresAt > now)
            .ExecuteUpdateAsync(x => x.SetProperty(t => t.ConsumedAt, now), ct);
        if (claimed != 1) throw Invalid();
        var session = await sessions.CreateAsync(transaction.AccountId.Value, transaction.SocialSessionId.Value, ct);
        await atomic.CommitAsync(ct);
        return new(session, transaction.ReturnUrl);
    }
}
