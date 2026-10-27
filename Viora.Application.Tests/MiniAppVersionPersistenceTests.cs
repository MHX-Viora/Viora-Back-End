using Microsoft.EntityFrameworkCore;
using Viora.Domain.Developers;
using Xunit;

public sealed class MiniAppVersionPersistenceTests
{
    [Fact]
    public async Task AnAppHasOneDraftAndUniqueServerVersionCodes()
    {
        await using var f = await SandboxFixture.Create();
        var first = Version(f.App);
        f.Db.Add(first);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        f.Db.Add(Version(f.App, 2));
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
        f.Db.ChangeTracker.Clear();
        await f.Db.Set<MiniAppVersion>().Where(x => x.Id == first.Id).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.State, MiniAppVersionState.Submitted).SetProperty(x => x.SubmittedAt, DateTime.UtcNow));
        f.Db.Add(Version(f.App));
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
        f.Db.ChangeTracker.Clear();
        f.Db.Add(Version(f.App, 2));
        await f.Db.SaveChangesAsync();
        Assert.Equal(2, await f.Db.Set<MiniAppVersion>().CountAsync());
    }

    [Fact]
    public async Task RevisionConflictsAndPinnedAssetsCannotBeDeleted()
    {
        await using var f = await SandboxFixture.Create();
        var logo = new MiniAppLogoUpload { DeveloperAccountId = f.App.DeveloperAccountId,
            MiniAppId = f.App.Id, PublicId = "version-logo", ExpiresAt = DateTime.UtcNow.AddDays(1) };
        var version = Version(f.App);
        version.LogoUploadId = logo.Id;
        f.Db.AddRange(logo, version);
        await f.Db.SaveChangesAsync();
        var nextRevision = Guid.NewGuid();
        await f.Db.Set<MiniAppVersion>().Where(x => x.Id == version.Id).ExecuteUpdateAsync(s =>
            s.SetProperty(x => x.ConcurrencyToken, nextRevision));
        version.VersionName = "Changed";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => f.Db.SaveChangesAsync());
        f.Db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            f.Db.Remove(logo);
            await f.Db.SaveChangesAsync();
        });
    }

    [Theory]
    [InlineData("code")]
    [InlineData("state")]
    [InlineData("hash")]
    [InlineData("submitted")]
    [InlineData("schema")]
    public async Task InvalidVersionCannotBePersisted(string invalid)
    {
        await using var f = await SandboxFixture.Create();
        var version = Version(f.App);
        switch (invalid)
        {
            case "code": version.VersionCode = 0; break;
            case "state": version.State = (MiniAppVersionState)99; break;
            case "hash": version.SnapshotHash = "invalid"; break;
            case "submitted": version.State = MiniAppVersionState.Submitted; break;
            case "schema": version.SnapshotSchemaVersion = 2; break;
        }
        f.Db.Add(version);
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
    }

    internal static MiniAppVersion Version(MiniApp app, long code = 1) => new()
    {
        MiniAppId = app.Id, CreatedByDeveloperId = app.DeveloperAccountId, VersionCode = code,
        VersionName = "1.0", SnapshotJson = "{}", SnapshotHash = new string('a', 64)
    };
}
