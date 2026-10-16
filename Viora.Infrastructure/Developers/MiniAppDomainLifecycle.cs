using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Developers;

internal static class MiniAppDomainLifecycle
{
    public const string Purpose = "mini-app-domain";
    public static string? Host(string? website)
    {
        if (string.IsNullOrWhiteSpace(website)) return null;
        try { return PublicWebAddress.Host(website); } catch (DeveloperAuthException) { return null; }
    }
    public static async Task Revoke(AppDbContext db, Guid appId, CancellationToken ct)
    {
        var version = Guid.NewGuid(); var now = DateTime.UtcNow;
        await db.MiniAppDomainVerifications.Where(x => x.MiniAppId == appId && x.RevokedAt == null).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.RevokedAt, now).SetProperty(x => x.Status, MiniAppDomainStatus.Revoked).SetProperty(x => x.Version, version), ct);
    }
    public static MiniAppDomainVerification Create(AppDbContext db, MiniApp app, string hostname, MiniAppSecretProtector protector)
    {
        var row = new MiniAppDomainVerification { MiniAppId = app.Id, Hostname = hostname, ExpiresAt = DateTime.UtcNow.AddDays(7), Status = MiniAppDomainStatus.Pending };
        row.VerificationFileName = $"ankt-verify-{row.Id:N}.txt";
        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        row.VerificationTokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        var encrypted = protector.Encrypt(token, row.Id, Purpose);
        row.VerificationMaterialEncrypted = encrypted.Ciphertext; row.SecretKeyVersion = encrypted.Version;
        db.MiniAppDomainVerifications.Add(row); return row;
    }
    public static async Task ChangeWebsite(AppDbContext db, MiniApp app, string? previousWebsite, MiniAppSecretProtector protector, CancellationToken ct)
    {
        var previous = Host(previousWebsite); var current = Host(app.WebsiteUrl);
        if (previous == current) return;
        await Revoke(db, app.Id, ct);
        if (current is not null) Create(db, app, current, protector);
        db.MiniAppAuditLogs.Add(new() { MiniAppId = app.Id, ActorDeveloperId = app.DeveloperAccountId, Action = "domain.configuration_changed" });
    }
}
