using Microsoft.EntityFrameworkCore;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Xunit;

public sealed class MiniAppVersionAssetTests
{
    [Theory]
    [InlineData(MiniAppVersionState.Draft)]
    [InlineData(MiniAppVersionState.Submitted)]
    [InlineData(MiniAppVersionState.Archived)]
    public async Task VersionReferencedAssetsNeverReachTheRemoteDeletionBoundary(MiniAppVersionState state)
    {
        await using var f = await SandboxFixture.Create(); var now = f.Clock.Now.UtcDateTime;
        var pinned = Upload(f.App.DeveloperAccountId, "pinned", now.AddHours(-1));
        var stale = Upload(f.App.DeveloperAccountId, "stale", now.AddHours(-1));
        var attached = Upload(f.App.DeveloperAccountId, "attached", now.AddHours(-1)); attached.MiniAppId = f.App.Id;
        var fresh = Upload(f.App.DeveloperAccountId, "fresh", now.AddHours(1));
        var version = MiniAppVersionPersistenceTests.Version(f.App); version.LogoUploadId = pinned.Id; version.State = state;
        if (state != MiniAppVersionState.Draft) version.SubmittedAt = now;
        f.Db.AddRange(pinned, stale, attached, fresh, version); await f.Db.SaveChangesAsync();
        var deleted = new List<string>();
        Assert.Equal(1, await MiniAppLogoCleanup.SweepAsync(f.Db, (id, _) => { deleted.Add(id); return Task.CompletedTask; }, now, default));
        Assert.Equal("stale", Assert.Single(deleted)); Assert.Equal(3, await f.Db.MiniAppLogoUploads.CountAsync());
        Assert.Equal(pinned.Id, await f.Db.MiniAppVersions.Select(x => x.LogoUploadId).SingleAsync());
    }

    [Fact]
    public async Task CleanupFailureKeepsTheTicketForRetryAndBatchSizeIsBounded()
    {
        await using var f = await SandboxFixture.Create(); var now = f.Clock.Now.UtcDateTime;
        f.Db.AddRange(Enumerable.Range(0, 101).Select(i => Upload(f.App.DeveloperAccountId, "stale-" + i, now.AddHours(-1))));
        await f.Db.SaveChangesAsync(); var retries = 0;
        Assert.Equal(0, await MiniAppLogoCleanup.SweepAsync(f.Db, (_, _) => throw new InvalidOperationException("fake storage failure"), now, default, () => retries++));
        Assert.Equal(100, retries); Assert.Equal(101, await f.Db.MiniAppLogoUploads.CountAsync());
        var deleted = new List<string>();
        Assert.Equal(100, await MiniAppLogoCleanup.SweepAsync(f.Db, (id, _) => { deleted.Add(id); return Task.CompletedTask; }, now, default));
        Assert.Equal(100, deleted.Distinct().Count()); Assert.Single(await f.Db.MiniAppLogoUploads.ToListAsync());
    }

    [Fact]
    public async Task PostgreSqlCleanupLocksAndDeletesOnlyUnreferencedExpiredTickets()
    {
        await using var pg = await IsolatedPostgres.Start(); await using var f = await SandboxFixture.Create(pg.ConnectionString);
        var now = f.Clock.Now.UtcDateTime; var stale = Upload(f.App.DeveloperAccountId, "pg-stale", now.AddHours(-1));
        f.Db.Add(stale); await f.Db.SaveChangesAsync();
        Assert.Equal(1, await MiniAppLogoCleanup.SweepAsync(f.Db, (id, _) => { Assert.Equal("pg-stale", id); return Task.CompletedTask; }, now, default));
        Assert.Empty(await f.Db.MiniAppLogoUploads.ToListAsync()); Assert.Single(await f.Db.MiniApps.ToListAsync());
    }

    private static MiniAppLogoUpload Upload(Guid owner, string id, DateTime expiry) => new()
    { DeveloperAccountId = owner, PublicId = id, Url = "https://assets.example.com/" + id + ".png", ExpiresAt = expiry };
}
