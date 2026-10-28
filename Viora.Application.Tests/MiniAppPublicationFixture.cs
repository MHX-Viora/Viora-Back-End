using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Domain.Entities;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;

// Shared publication fixture, independent of an authorization server.
internal static class MiniAppPublicationPolicyTests
{
    internal static async Task<MiniAppOAuthClient> ApprovedPublication(SandboxFixture f)
    {
        var client = new MiniAppOAuthClient { MiniAppId = f.App.Id, ClientKey = "fixture-production-client",
            ClientType = "Public", AllowedScopes = "openid", Environment = MiniAppOAuthEnvironment.Production };
        await Seal(f.Db, f.App, client, MiniAppVersionSnapshotTests.Snapshot() with {
            Sandbox = null, Production = new(client.Id, client.ClientKey, "Public", ["openid"], ["https://partner.example.com/callback"], false)
        }, f.Clock.Now.UtcDateTime);
        return client;
    }

    internal static async Task Seal(AppDbContext db, MiniApp app, MiniAppOAuthClient client,
        MiniAppVersionSnapshot snapshot, DateTime now)
    {
        if (!await db.MiniAppOAuthClients.AnyAsync(x => x.Id == client.Id)) db.Add(client);
        var admin = new Account { Email = "publication-admin@example.test", Role = AccountRole.Admin };
        db.Add(admin);
        var canonical = MiniAppVersionSnapshotPolicy.Create(snapshot);
        var version = new MiniAppVersion { MiniAppId = app.Id, CreatedByDeveloperId = app.DeveloperAccountId,
            VersionCode = 1, VersionName = "1.0", SnapshotJson = canonical.Json, SnapshotHash = canonical.Hash,
            State = MiniAppVersionState.Submitted, SubmittedAt = now };
        db.Add(version); await db.SaveChangesAsync();
        var review = new MiniAppReviewRequest { MiniAppId = app.Id, MiniAppVersionId = version.Id,
            DeveloperAccountId = app.DeveloperAccountId, Status = MiniAppReviewStatus.PendingReview,
            SubmittedAt = now, EvidenceAttestationJson = "{}", CorrelationId = Guid.NewGuid() };
        db.Add(review); await db.SaveChangesAsync();
        review.Status = MiniAppReviewStatus.InReview; review.AssignedAdminAccountId = admin.Id;
        review.ReviewStartedAt = now; await db.SaveChangesAsync();
        review.Status = MiniAppReviewStatus.Approved; review.DecidedAt = now; await db.SaveChangesAsync();
        db.AddRange(new MiniAppPublication { MiniAppId = app.Id, MiniAppVersionId = version.Id,
            ApprovedReviewRequestId = review.Id, PublishedByDeveloperId = app.DeveloperAccountId,
            ProductionOAuthClientId = snapshot.SsoEnabled ? client.Id : null, PublishedAt = now },
            new MiniAppOperationalState { MiniAppId = app.Id });
        await db.SaveChangesAsync();
    }
}
