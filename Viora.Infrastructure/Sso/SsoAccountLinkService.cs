using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using Viora.Application.Developers;
using Viora.Application.Sso;
using Viora.Domain.Developers;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Sso;

public sealed class SsoAccountLinkService(AppDbContext db, SsoClientReader clients, SsoConsentService consents,
    IOpenIddictApplicationManager applications, TimeProvider clock)
{
    public async Task<SsoAccountLinkProof> IssueAsync(string clientId, string secret, string subject, string partnerAccountId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(partnerAccountId) || partnerAccountId.Length > 128 || partnerAccountId.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(subject) || subject.Length > 100 || string.IsNullOrEmpty(secret)) throw Denied();
        // One snapshot binds the verified projection secret to its registry credential.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var client = await clients.EligibleAsync(clientId, ["openid"], null, ct);
        var application = await applications.FindByClientIdAsync(clientId, ct);
        if (client.ClientType != "Confidential" || application == null || !await applications.ValidateClientSecretAsync(application, secret, ct)) throw Denied();
        var credentialId = await db.MiniAppClientSecrets.Where(x => x.OAuthClientId == client.Id && x.RevokedAt == null &&
            (x.ExpiresAt == null || x.ExpiresAt > clock.GetUtcNow().UtcDateTime)).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct) ?? throw Denied();
        var mapping = await db.MiniAppPairwiseSubjects.AsNoTracking().SingleOrDefaultAsync(x => x.MiniAppId == client.MiniAppId &&
            x.Environment == client.Environment && x.Subject == subject, ct) ?? throw Denied();
        await RequireConsent(mapping.AccountId, clientId, subject, ct);
        var proof = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var expiry = clock.GetUtcNow().UtcDateTime.AddMinutes(2);
        db.Set<SsoAccountLinkChallenge>().Add(new() { AccountId = mapping.AccountId, MiniAppId = client.MiniAppId,
            OAuthClientId = client.Id, ClientSecretId = credentialId, Environment = client.Environment, Subject = subject, PartnerAccountId = partnerAccountId,
            ProofHash = Hash(proof), ExpiresAt = expiry });
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return new(proof, expiry);
    }

    public async Task<SsoAccountLinkPreview> PreviewAsync(Guid owner, string proof, CancellationToken ct)
    {
        var (challenge, client) = await ValidateAsync(owner, proof, ct);
        return new(client.MiniApp.AppId, client.MiniApp.Name, challenge.Environment.ToString(), challenge.PartnerAccountId, challenge.ExpiresAt);
    }

    public async Task<SsoAccountLinkDto> ConfirmAsync(Guid owner, string proof, CancellationToken ct)
    {
        try { return await ConfirmCoreAsync(owner,proof,ct); }
        catch (InvalidOperationException error) when (error.InnerException is Npgsql.PostgresException {SqlState: "40001" or "40P01"})
        {
            // EF's Npgsql execution strategy wraps transient errors from bulk updates.
            // The failed transaction has been disposed; retry requires a fresh caller request.
            throw new DeveloperAuthException(409,"SSO_LINK_CONFLICT","Liên kết vừa thay đổi. Vui lòng thử lại.");
        }
    }
    private async Task<SsoAccountLinkDto> ConfirmCoreAsync(Guid owner, string proof, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var (challenge, client) = await ValidateAsync(owner, proof, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var consumed = await db.Set<SsoAccountLinkChallenge>().Where(x => x.Id == challenge.Id && x.AccountId == owner &&
            x.ConsumedAt == null && x.ExpiresAt > now).ExecuteUpdateAsync(x => x.SetProperty(p => p.ConsumedAt, now), ct);
        if (consumed != 1) throw Denied();
        var links = db.Set<MiniAppAccountLink>();
        var link = await links.SingleOrDefaultAsync(x => x.MiniAppId == challenge.MiniAppId && x.Environment == challenge.Environment && x.AccountId == owner, ct);
        if (await links.AnyAsync(x => x.MiniAppId == challenge.MiniAppId && x.Environment == challenge.Environment &&
            x.PartnerAccountId == challenge.PartnerAccountId && x.AccountId != owner, ct)) throw Denied();
        if (link != null && (link.PartnerAccountId != challenge.PartnerAccountId || link.Subject != challenge.Subject)) throw Denied();
        var action = link == null ? "link" : "relink";
        if (link == null)
        {
            link = new() { AccountId = owner, MiniAppId = challenge.MiniAppId, Environment = challenge.Environment,
                Subject = challenge.Subject, PartnerAccountId = challenge.PartnerAccountId, LinkedAt = now };
            links.Add(link);
        }
        else if (link.UnlinkedAt != null)
        {
            link.UnlinkedAt = null; link.LinkedAt = now; link.Version = Guid.NewGuid();
        }
        else action = ""; // Repeated explicit proof for an already linked pair changes no history.
        if (action.Length != 0) Audit(link, action);
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return Map(link, client.MiniApp);
    }

    public async Task<IReadOnlyList<SsoAccountLinkDto>> ListAsync(Guid owner, CancellationToken ct)
    {
        var rows = await (from link in db.Set<MiniAppAccountLink>().AsNoTracking()
            join app in db.MiniApps.AsNoTracking() on link.MiniAppId equals app.Id
            where link.AccountId == owner orderby link.LinkedAt descending, link.Id select new { Link = link, App = app }).Take(100).ToListAsync(ct);
        return rows.Select(x => Map(x.Link, x.App)).ToArray();
    }

    public async Task<bool> UnlinkAsync(Guid owner, Guid id, CancellationToken ct)
    {
        var link = await db.Set<MiniAppAccountLink>().SingleOrDefaultAsync(x => x.Id == id && x.AccountId == owner, ct);
        if (link == null) return false;
        if (link.UnlinkedAt == null)
        {
            link.UnlinkedAt = clock.GetUtcNow().UtcDateTime; link.Version = Guid.NewGuid(); Audit(link, "unlink");
            await db.SaveChangesAsync(ct);
        }
        return true;
    }

    private async Task<(SsoAccountLinkChallenge, MiniAppOAuthClient)> ValidateAsync(Guid owner, string proof, CancellationToken ct)
    {
        if (proof == null || proof.Length != 43) throw Denied();
        var hash = Hash(proof); var now = clock.GetUtcNow().UtcDateTime;
        var challenge = await db.Set<SsoAccountLinkChallenge>().AsNoTracking().SingleOrDefaultAsync(x => x.ProofHash == hash &&
            x.AccountId == owner && x.ConsumedAt == null && x.ExpiresAt > now, ct) ?? throw Denied();
        var clientId = await db.MiniAppOAuthClients.Where(x => x.Id == challenge.OAuthClientId).Select(x => x.ClientKey).SingleOrDefaultAsync(ct);
        var client = await clients.EligibleAsync(clientId, ["openid"], challenge.Environment.ToString(), ct);
        if (client.ClientType != "Confidential" || client.MiniAppId != challenge.MiniAppId) throw Denied();
        if (!await db.MiniAppClientSecrets.AnyAsync(x => x.Id == challenge.ClientSecretId && x.OAuthClientId == client.Id &&
            x.RevokedAt == null && (x.ExpiresAt == null || x.ExpiresAt > now), ct)) throw Denied();
        await RequireConsent(owner, client.ClientKey, challenge.Subject, ct);
        return (challenge, client);
    }
    private async Task RequireConsent(Guid owner, string clientId, string subject, CancellationToken ct)
    {
        var grant = await consents.FindAsync(owner, clientId, ["openid"], ct);
        if (grant?.Subject != subject || !await db.Accounts.AnyAsync(x => x.Id == owner && x.Status == AccountStatus.Active && x.DeletedAt == null, ct)) throw Denied();
    }
    private void Audit(MiniAppAccountLink link, string action) => db.Set<SsoAccountLinkAudit>().Add(new() {
        AccountId = link.AccountId, MiniAppId = link.MiniAppId, Environment = link.Environment, LinkId = link.Id, Action = action });
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static SsoAccountLinkDto Map(MiniAppAccountLink link, MiniApp app) => new(link.Id, app.AppId, app.Name,
        link.Environment.ToString(), link.Subject, link.PartnerAccountId, link.LinkedAt, link.UnlinkedAt);
    private static DeveloperAuthException Denied() => new(403, "SSO_LINK_UNAVAILABLE", "Không thể liên kết tài khoản. Hãy bắt đầu lại từ ứng dụng đối tác.");
}
