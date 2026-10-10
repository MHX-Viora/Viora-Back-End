using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

public sealed class IdentityTests
{
    [Fact]
    public async Task LoginCreatesAccountWithStableSubjectAndNoEmailMatching()
    {
        await using var fixture = await Fixture.Create();
        var id = await fixture.Service.ResolveAsync("sub_new", "Existing", "login", null, default);
        Assert.NotEqual(fixture.Existing.Id, id);
        Assert.Equal(id, await fixture.Service.ResolveAsync("sub_new", "Changed", "login", null, default));
        Assert.Null((await fixture.Db.Users.FindAsync(id))!.PasswordHash);
    }
    [Fact]
    public async Task LinkRequiresAuthenticatedExistingAccount()
    {
        await using var fixture = await Fixture.Create();
        await Assert.ThrowsAsync<PartnerLinkConflict>(() => fixture.Service.ResolveAsync("sub_new", null, "link", null, default));
        await Assert.ThrowsAsync<PartnerLinkConflict>(() => fixture.Service.ResolveAsync("sub_new", null, "link", Guid.NewGuid(), default));
    }
    [Fact]
    public async Task ExplicitLinkIsIdempotentAndPreservesExistingAccount()
    {
        await using var fixture = await Fixture.Create();
        Assert.Equal(fixture.Existing.Id, await fixture.Service.ResolveAsync("sub_link", null, "link", fixture.Existing.Id, default));
        Assert.Equal(fixture.Existing.Id, await fixture.Service.ResolveAsync("sub_link", null, "link", fixture.Existing.Id, default));
        Assert.Equal(fixture.Existing.Id, await fixture.Service.ResolveAsync("sub_link", null, "login", null, default));
    }
    [Fact]
    public async Task SameSubjectCannotLinkToAnotherAccount()
    {
        await using var fixture = await Fixture.Create();
        await fixture.Service.ResolveAsync("sub_link", null, "link", fixture.Existing.Id, default);
        var other = new PartnerUser { Username = "other", DisplayName = "Other", PasswordHash = "verified" }; fixture.Db.Users.Add(other); await fixture.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<PartnerLinkConflict>(() => fixture.Service.ResolveAsync("sub_link", null, "link", other.Id, default));
        Assert.Single(await fixture.Db.Identities.ToListAsync());
    }
    [Fact]
    public async Task ExistingLinkCannotSilentlySwitchSubject()
    {
        await using var fixture = await Fixture.Create();
        await fixture.Service.ResolveAsync("sub_link", null, "link", fixture.Existing.Id, default);
        await Assert.ThrowsAsync<PartnerLinkConflict>(() => fixture.Service.ResolveAsync("sub_other", null, "link", fixture.Existing.Id, default));
    }
    [Fact]
    public async Task UnlinkPreservesLocalAccountAndDisablesIdentityAccess()
    {
        await using var fixture = await Fixture.Create();
        await fixture.Service.ResolveAsync("sub_link", null, "link", fixture.Existing.Id, default);
        await fixture.Service.UnlinkAsync(fixture.Existing.Id, default);
        // Subsequent API requests use a fresh scoped context; bulk deletes bypass EF tracking.
        fixture.Db.ChangeTracker.Clear();
        Assert.Empty(await fixture.Db.Identities.ToListAsync()); Assert.NotNull(await fixture.Db.Users.FindAsync(fixture.Existing.Id));
        Assert.NotEqual(fixture.Existing.Id, await fixture.Service.ResolveAsync("sub_link", null, "login", null, default));
    }
    [Fact]
    public async Task UnlinkCannotRemoveLastLoginMethod()
    {
        await using var fixture = await Fixture.Create();
        var id = await fixture.Service.ResolveAsync("sub_new", null, "login", null, default);
        await Assert.ThrowsAsync<PartnerLinkConflict>(() => fixture.Service.UnlinkAsync(id, default));
        Assert.Equal(id, await fixture.Service.ResolveAsync("sub_new", null, "login", null, default));
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public required SqliteConnection Connection { get; init; }
        public required PartnerDb Db { get; init; }
        public required PartnerUser Existing { get; init; }
        public required PartnerIdentityService Service { get; init; }
        public static async Task<Fixture> Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
            var db = new PartnerDb(new DbContextOptionsBuilder<PartnerDb>().UseSqlite(connection).Options); await db.Database.EnsureCreatedAsync();
            var user = new PartnerUser { Username = "existing", DisplayName = "Existing", PasswordHash = "verified" }; db.Users.Add(user); await db.SaveChangesAsync();
            return new() {Connection = connection, Db = db, Existing = user, Service = new(db)};
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await Connection.DisposeAsync(); }
    }
}
