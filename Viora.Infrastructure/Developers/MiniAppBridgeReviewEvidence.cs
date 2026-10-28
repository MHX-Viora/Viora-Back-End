using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Developers;

// Reads server-verified Bridge exchange proof, never browser-reported success.
public sealed class MiniAppBridgeReviewEvidence(AppDbContext db, MiniAppBridgeService bridge, TimeProvider clock)
{
    public async Task<MiniAppBridgeSession?> ReadAsync(MiniApp app, Guid owner, Guid source, Guid? id, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var session = await db.Set<MiniAppBridgeSession>().AsNoTracking().Where(x => x.MiniAppId == app.Id &&
            x.IsSandbox && x.ActorId == owner && x.SourceSessionId == source && x.ClientId == app.AppId &&
            (id == null || x.Id == id) && x.RevokedAt == null && x.ExpiresAt > now && x.AccessExpiresAt > now &&
            x.ConsumedAt >= now.AddMinutes(-5) && x.ConsumedAt <= now && x.AccessHash != null)
            .OrderByDescending(x => x.ConsumedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        if (session == null) return null;
        try
        {
            session = await bridge.ValidatePartnerSourceAsync(session.Id, ct);
            await bridge.ProfileAsync(session, ct);
            return session;
        }
        catch (DeveloperAuthException) { return null; }
    }

    public static string Fingerprint(MiniApp app, MiniAppVersion version) =>
        MiniAppBridgeService.Hash("bridge-review-v2:" + app.Id + ":" + app.Version + ":" + version.SnapshotHash);

    public static bool Applicable(SandboxCheckKey key) => key is not (SandboxCheckKey.Callbacks or
        SandboxCheckKey.SsoClient or SandboxCheckKey.Discovery or SandboxCheckKey.Consent);
    public static bool Protocol(SandboxCheckKey key) => key is SandboxCheckKey.Runtime or SandboxCheckKey.Bridge or
        SandboxCheckKey.Token or SandboxCheckKey.UserInfo;

    public static SandboxReadinessCheck[] Checks(MiniAppVersionSnapshot snapshot, MiniAppBridgeSession? session,
        bool publicPagesAvailable, DateTime now) => Enum.GetValues<SandboxCheckKey>().Select(key =>
    {
        var applicable = Applicable(key); var protocol = Protocol(key);
        var passed = protocol ? session != null : key switch
        {
            SandboxCheckKey.Logo => !string.IsNullOrWhiteSpace(snapshot.LogoUrl),
            SandboxCheckKey.Name => !string.IsNullOrWhiteSpace(snapshot.Name),
            SandboxCheckKey.Category => MiniAppInput.Categories.Contains(snapshot.Category),
            SandboxCheckKey.Description => !string.IsNullOrWhiteSpace(snapshot.ShortDescription) && !string.IsNullOrWhiteSpace(snapshot.Description),
            SandboxCheckKey.SupportEmail => !string.IsNullOrWhiteSpace(snapshot.SupportEmail),
            SandboxCheckKey.Domain => session != null,
            SandboxCheckKey.Environment => session != null,
            _ => publicPagesAvailable
        };
        return new SandboxReadinessCheck(char.ToLowerInvariant(key.ToString()[0]) + key.ToString()[1..], applicable,
            !applicable ? "Skipped" : passed ? "Passed" : "Failed",
            MiniAppSandboxDiagnosticCatalog.Code(!applicable ? SandboxMessageCode.CheckNotApplicable : passed ? SandboxMessageCode.CheckPassed : SandboxMessageCode.CheckFailed),
            applicable && protocol ? "ServerProtocol" : "Server", !applicable ? null : protocol ? session?.ConsumedAt : now);
    }).ToArray();
}
