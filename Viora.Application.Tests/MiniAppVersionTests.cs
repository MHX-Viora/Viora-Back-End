using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Xunit;

public sealed class MiniAppVersionTests
{
    [Fact]
    public async Task InvalidProductionIntentRollsBackTheWorkingFlagAndRevision()
    {
        await using var f = await SandboxFixture.Create(); var service = Service(f);
        var draft = await service.CreateAsync(f.Actor.Id, f.Source.Id, f.App.AppId, new(Guid.NewGuid(), f.App.Version, "1.0"), default);
        var before = await f.Db.MiniApps.AsNoTracking().SingleAsync();
        Assert.Equal(400, (await Assert.ThrowsAsync<DeveloperAuthException>(() => service.UpdateDraftAsync(f.Actor.Id, f.Source.Id, f.App.AppId, draft.Id,
            new(Guid.NewGuid(), draft.ConcurrencyToken, ""), default))).Status);
        var after = await f.Db.MiniApps.AsNoTracking().SingleAsync();
        Assert.Equal(before.Version, after.Version); Assert.Equal(before.SsoEnabled, after.SsoEnabled);
        Assert.Equal(draft.ConcurrencyToken, (await service.GetAsync(f.Actor.Id, f.Source.Id, f.App.AppId, draft.Id, default)).Version.ConcurrencyToken);
        Assert.Single(await f.Db.MiniAppReviewActions.ToListAsync());
    }
    internal static MiniAppVersionService Service(SandboxFixture f) => new(f.Db, new(f.Db, f.Clock), f.Clock);

    [Fact]
    public async Task DraftUsesServerConfigurationAndReplayReturnsOriginalResult()
    {
        await using var f = await SandboxFixture.Create(); var service = Service(f);
        var request = new MiniAppVersionCreateRequest(Guid.NewGuid(), f.App.Version, "1.0");
        var created = await service.CreateAsync(f.Actor.Id, f.Source.Id, f.App.AppId, request, default);
        Assert.Equal(1, created.VersionCode); Assert.Equal("Draft", created.State);
        var detail = await service.GetAsync(f.Actor.Id, f.Source.Id, f.App.AppId, created.Id, default);
        Assert.Equal(f.App.Name, detail.Snapshot.Name); Assert.Null(detail.Snapshot.Sandbox); Assert.Null(detail.Snapshot.Production); Assert.False(detail.Snapshot.SsoEnabled);
        Assert.Equal(created, await service.CreateAsync(f.Actor.Id, f.Source.Id, f.App.AppId, request, default));
        Assert.Single(await f.Db.MiniAppReviewActions.ToListAsync());
        var conflict = await Assert.ThrowsAsync<DeveloperAuthException>(() => service.CreateAsync(f.Actor.Id, f.Source.Id,
            f.App.AppId, request with { VersionName = "different" }, default));
        Assert.Equal(409, conflict.Status);
        f.Source.RevokedAt = f.Clock.Now.UtcDateTime; f.Db.Update(f.Source); await f.Db.SaveChangesAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<DeveloperAuthException>(() => service.CreateAsync(f.Actor.Id,
            f.Source.Id, f.App.AppId, request, default))).Status);
    }

    [Fact]
    public async Task DraftRevisionAndAppRevisionPreventLostUpdatesAndLiveClientsStayUnchanged()
    {
        await using var f = await SandboxFixture.Create(); var service = Service(f);
        var created = await service.CreateAsync(f.Actor.Id, f.Source.Id, f.App.AppId,
            new(Guid.NewGuid(), f.App.Version, "1.0"), default);
        var update = new MiniAppVersionUpdateRequest(Guid.NewGuid(), created.ConcurrencyToken, "1.1");
        var updated = await service.UpdateDraftAsync(f.Actor.Id, f.Source.Id, f.App.AppId, created.Id, update, default);
        Assert.NotEqual(created.ConcurrencyToken, updated.ConcurrencyToken);
        Assert.Equal(updated, await service.UpdateDraftAsync(f.Actor.Id, f.Source.Id, f.App.AppId, created.Id, update, default));
        Assert.Equal(409, (await Assert.ThrowsAsync<DeveloperAuthException>(() => service.UpdateDraftAsync(f.Actor.Id,
            f.Source.Id, f.App.AppId, created.Id, update with { RequestId = Guid.NewGuid() }, default))).Status);
        var detail = await service.GetAsync(f.Actor.Id, f.Source.Id, f.App.AppId, created.Id, default);
        Assert.Equal("1.1", detail.Version.VersionName); Assert.Null(detail.Snapshot.Production);
        Assert.Equal("Public", await f.Db.MiniAppOAuthClients.Select(x => x.ClientType).SingleAsync());
        Assert.Empty(await f.Db.Set<MiniAppClientSecret>().ToListAsync()); Assert.Empty(await f.Db.MiniAppPublications.ToListAsync());
    }

    [Fact]
    public async Task SubmittedVersionIsFrozenAndNextDraftGetsNewCodeWithoutReplacingHistory()
    {
        await using var f = await SandboxFixture.Create(); var service = Service(f);
        var first = await service.CreateAsync(f.Actor.Id, f.Source.Id, f.App.AppId,
            new(Guid.NewGuid(), f.App.Version, "1.0"), default);
        await f.Db.MiniAppVersions.Where(x => x.Id == first.Id).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.State, MiniAppVersionState.Submitted).SetProperty(x => x.SubmittedAt, f.Clock.Now.UtcDateTime));
        Assert.Equal(409, (await Assert.ThrowsAsync<DeveloperAuthException>(() => service.UpdateDraftAsync(f.Actor.Id,
            f.Source.Id, f.App.AppId, first.Id, new(Guid.NewGuid(), first.ConcurrencyToken, "tampered"), default))).Status);
        f.Db.ChangeTracker.Clear(); var app = await f.Db.MiniApps.SingleAsync();
        app.Name = "New working name"; app.Version = Guid.NewGuid(); await f.Db.SaveChangesAsync();
        var second = await service.CreateAsync(f.Actor.Id, f.Source.Id, f.App.AppId, new(Guid.NewGuid(), app.Version, "2.0"), default);
        Assert.Equal(2, second.VersionCode);
        Assert.Equal("Fixture", (await service.GetAsync(f.Actor.Id, f.Source.Id, f.App.AppId, first.Id, default)).Snapshot.Name);
        Assert.Equal("New working name", (await service.GetAsync(f.Actor.Id, f.Source.Id, f.App.AppId, second.Id, default)).Snapshot.Name);
        Assert.Equal(2, (await service.ListAsync(f.Actor.Id, f.Source.Id, f.App.AppId, 1, 20, default)).Total);
    }

    [Fact]
    public async Task OwnershipBoundsInvalidPayloadAndTamperedSnapshotsFailClosed()
    {
        await using var f = await SandboxFixture.Create(); var service = Service(f);
        var request = new MiniAppVersionCreateRequest(Guid.NewGuid(), f.App.Version, "1.0");
        Assert.Equal(404, (await Assert.ThrowsAsync<DeveloperAuthException>(() => service.CreateAsync(Guid.NewGuid(),
            f.Source.Id, f.App.AppId, request, default))).Status);
        Assert.Equal(409, (await Assert.ThrowsAsync<DeveloperAuthException>(() => service.CreateAsync(f.Actor.Id,
            f.Source.Id, f.App.AppId, request with { AppVersion = Guid.NewGuid() }, default))).Status);
        Assert.Empty(await f.Db.MiniAppVersions.ToListAsync());
        Assert.Equal(400, (await Assert.ThrowsAsync<DeveloperAuthException>(() => service.ListAsync(f.Actor.Id,
            f.Source.Id, f.App.AppId, 1, 101, default))).Status);
        var created = await service.CreateAsync(f.Actor.Id, f.Source.Id, f.App.AppId, request, default);
        await f.Db.MiniAppVersions.Where(x => x.Id == created.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.SnapshotHash, new string('0', 64)));
        Assert.Equal("VERSION_SNAPSHOT_INVALID", (await Assert.ThrowsAsync<DeveloperAuthException>(() => service.GetAsync(
            f.Actor.Id, f.Source.Id, f.App.AppId, created.Id, default))).Code);
    }
}
