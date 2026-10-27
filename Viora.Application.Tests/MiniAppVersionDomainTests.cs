using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.Developers;
using Xunit;

public sealed class MiniAppVersionDomainTests
{
    [Fact]
    public async Task DraftWebsiteChangesPreserveReviewedHostnameAndReuseItsExistingProof()
    {
        await using var host = await MiniAppApiTests.Host.Create(review: true); var http = host.Client;
        http.DefaultRequestHeaders.Authorization = new("DeveloperSession", await host.Login("version-domain@example.test"));
        var app = (await (await http.PostAsJsonAsync("/api/developer/mini-apps", new MiniAppCreateRequest("Domain versions", "utilities", Guid.NewGuid()))).Content.ReadFromJsonAsync<MiniAppDto>())!;
        var root = "/api/developer/mini-apps/" + app.AppId;
        Assert.Equal(HttpStatusCode.OK, (await http.PatchAsJsonAsync(root, new { websiteUrl = "https://partner.example.com/app" })).StatusCode);
        var revision = await host.WithServices(async s => await s.GetRequiredService<AppDbContext>().MiniApps.Select(x => x.Version).SingleAsync());
        var draft = (await (await http.PostAsJsonAsync(root + "/versions", new MiniAppVersionCreateRequest(Guid.NewGuid(), revision, "1.0"))).Content.ReadFromJsonAsync<MiniAppVersionSummaryDto>())!;
        var original = await host.WithServices(async s =>
        {
            var db = s.GetRequiredService<AppDbContext>(); var model = await db.MiniApps.SingleAsync(); var proof = await db.MiniAppDomainVerifications.SingleAsync();
            proof.Status = MiniAppDomainStatus.Verified; proof.VerifiedAt = DateTime.UtcNow; proof.ExpiresAt = DateTime.UtcNow.AddHours(1);
            await db.MiniAppVersions.Where(x => x.Id == draft.Id).ExecuteUpdateAsync(set => set.SetProperty(x => x.State, MiniAppVersionState.Submitted).SetProperty(x => x.SubmittedAt, DateTime.UtcNow));
            // Persisted ownership/review fixtures only, not actual DNS or readiness evidence.
            db.MiniAppReviewRequests.Add(MiniAppReviewPersistenceTests.Review(model, draft.Id)); await db.SaveChangesAsync();
            return proof.Id;
        });
        Assert.Equal(HttpStatusCode.OK, (await http.PatchAsJsonAsync(root, new { websiteUrl = "https://other.example.com/app" })).StatusCode);
        var current = (await http.GetFromJsonAsync<MiniAppDomainDto>(root + "/domain-verification"))!;
        Assert.Equal("other.example.com", current.Hostname);
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsJsonAsync(root + "/domain-verification/regenerate", new { })).StatusCode);
        await host.WithServices(async s =>
        {
            var db = s.GetRequiredService<AppDbContext>(); var old = await db.MiniAppDomainVerifications.SingleAsync(x => x.Id == original);
            Assert.Null(old.RevokedAt); Assert.Equal(MiniAppDomainStatus.Verified, old.Status);
            Assert.Equal(2, await db.MiniAppDomainVerifications.CountAsync(x => x.RevokedAt == null)); return true;
        });
        Assert.Equal(HttpStatusCode.OK, (await http.PatchAsJsonAsync(root, new { websiteUrl = "https://partner.example.com/app" })).StatusCode);
        await host.WithServices(async s =>
        {
            var db = s.GetRequiredService<AppDbContext>(); var active = await db.MiniAppDomainVerifications.SingleAsync(x => x.RevokedAt == null);
            Assert.Equal(original, active.Id); Assert.Equal(MiniAppDomainStatus.Verified, active.Status);
            Assert.Equal(3, await db.MiniAppDomainVerifications.CountAsync()); return true;
        });
    }

    [Fact]
    public async Task DatabaseAllowsDifferentActiveHostsButRejectsTwoProofsForTheSameHost()
    {
        await using var f = await SandboxFixture.Create();
        f.Db.MiniAppDomainVerifications.Add(Proof(f.App.Id, "other.example.com", "other-host.txt")); await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        f.Db.MiniAppDomainVerifications.Add(Proof(f.App.Id, "other.example.com", "duplicate-host.txt"));
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
    }

    internal static MiniAppDomainVerification Proof(Guid app, string host, string filename) => new()
    {
        MiniAppId = app, Hostname = host, VerificationFileName = filename, Status = MiniAppDomainStatus.Verified,
        ExpiresAt = DateTime.UtcNow.AddHours(1), VerificationTokenHash = new string('a', 64),
        VerificationMaterialEncrypted = "fixture-only", SecretKeyVersion = "fixture"
    };

    [Fact]
    public async Task SandboxLaunchSelectsItsActualHostnameAndNeverFallsBackToAnotherVerifiedHost()
    {
        await using var f = await SandboxFixture.Create();
        f.Db.Add(Proof(f.App.Id, "other.example.com", "sandbox-other-host.txt")); await f.Db.SaveChangesAsync();
        var session = await f.Service().CreateAsync(f.Actor, f.App.AppId, default);
        Assert.Equal("https://partner.example.com/app", await f.Db.MiniAppSandboxSessions.Where(x => x.Id == session.Id).Select(x => x.WebsiteUrl).SingleAsync());
        await f.Service().RevokeAsync(f.Actor, f.App.AppId, session.Id, default);
        f.Proof.RevokedAt = f.Clock.Now.UtcDateTime; f.Proof.Status = MiniAppDomainStatus.Revoked; await f.Db.SaveChangesAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<DeveloperAuthException>(() => f.Service().CreateAsync(f.Actor, f.App.AppId, default))).Status);
    }
}
