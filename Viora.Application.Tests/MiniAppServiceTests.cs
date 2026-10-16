using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class MiniAppServiceTests
{
    [Fact]
    public async Task CreateIsAtomicIdempotentAndCredentialsAreEncrypted()
    {
        await using var f = await Fixture.Create();
        var request = new MiniAppCreateRequest("Test app", "utilities", Guid.NewGuid());
        var app = await f.Service.Create(f.Owner, request, default);
        Assert.Equal(app.AppId, (await f.Service.Create(f.Owner, request, default)).AppId);
        Assert.Single(await f.Db.MiniApps.ToListAsync()); Assert.Single(await f.Db.MiniAppOAuthClients.ToListAsync());
        var credentials = await f.Service.Credentials(f.Owner, app.AppId, false, default);
        var encrypted = await f.Db.MiniAppClientSecrets.SingleAsync();
        Assert.NotEqual(credentials.ClientSecret, encrypted.SecretEncrypted);
        Assert.Equal(credentials.ClientSecret, (await f.Service.Credentials(f.Owner, app.AppId, false, default)).ClientSecret);
        Assert.DoesNotContain(credentials.ClientSecret, System.Text.Json.JsonSerializer.Serialize(await f.Service.List(f.Owner, 1, default)));
        Assert.DoesNotContain(credentials.ClientSecret, System.Text.Json.JsonSerializer.Serialize(await f.Service.Get(f.Owner, app.AppId, default)));
        Assert.All(await f.Db.MiniAppAuditLogs.ToListAsync(), a => Assert.DoesNotContain(credentials.ClientSecret, a.Metadata));
        var conflict = await Assert.ThrowsAsync<DeveloperAuthException>(() => f.Service.Create(f.Owner, request with { Name = "Changed" }, default));
        Assert.Equal(409, conflict.Status);
        var second = await f.Service.Create(f.Owner, request with { RequestId = Guid.NewGuid() }, default);
        var otherCredentials = await f.Service.Credentials(f.Owner, second.AppId, false, default);
        Assert.NotEqual(app.AppId, second.AppId); Assert.NotEqual(credentials.ClientKey, otherCredentials.ClientKey);
    }

    [Fact]
    public async Task RotationRevokesOldSecretAndPreservesIdentifiers()
    {
        await using var f = await Fixture.Create(); var app = await f.App();
        var before = await f.Service.Credentials(f.Owner, app.AppId, false, default);
        var after = await f.Service.Credentials(f.Owner, app.AppId, true, default);
        Assert.Equal(before.AppId, after.AppId); Assert.Equal(before.ClientKey, after.ClientKey);
        Assert.NotEqual(before.ClientSecret, after.ClientSecret);
        Assert.Single(await f.Db.MiniAppClientSecrets.Where(x => x.RevokedAt == null).ToListAsync());
        Assert.Single(await f.Db.MiniAppClientSecrets.Where(x => x.RevokedAt != null).ToListAsync());
        Assert.Contains(await f.Service.Activity(f.Owner, app.AppId, default), a => a.Action == "credentials.rotated");
    }

    [Fact]
    public async Task EveryOperationRejectsOtherOwner()
    {
        await using var f = await Fixture.Create(); var app = await f.App(); var other = Guid.NewGuid();
        Func<Task>[] operations = [
            async () => { await f.Service.Get(other, app.AppId, default); },
            async () => { await f.Service.Update(other, app.AppId, new(Name: "Hijacked"), default); },
            () => f.Service.Delete(other, app.AppId, default),
            async () => { await f.Service.Credentials(other, app.AppId, false, default); },
            async () => { await f.Service.Credentials(other, app.AppId, true, default); },
            async () => { await f.Service.Redirects(other, app.AppId, default); },
            async () => { await f.Service.SaveRedirects(other, app.AppId, new(["https://example.com/cb"]), default); },
            async () => { await f.Service.Activity(other, app.AppId, default); },
        ];
        foreach (var operation in operations) Assert.Equal(404, (await Assert.ThrowsAsync<DeveloperAuthException>(operation)).Status);
        var list = System.Text.Json.JsonSerializer.Serialize(await f.Service.List(other, 1, default));
        Assert.DoesNotContain(app.AppId, list);
    }

    [Fact]
    public async Task DraftCallbacksAndSoftDeletionPersist()
    {
        await using var f = await Fixture.Create(); var app = await f.App();
        await f.Service.Update(f.Owner, app.AppId, new(ShortDescription: "A saved draft", WebsiteUrl: "https://example.com"), default);
        await f.Service.SaveRedirects(f.Owner, app.AppId, new(["https://example.com/cb", "https://example.com/cb"]), default);
        f.Db.ChangeTracker.Clear();
        Assert.Equal("A saved draft", (await f.Service.Get(f.Owner, app.AppId, default)).ShortDescription);
        Assert.Single(await f.Service.Redirects(f.Owner, app.AppId, default));
        await Assert.ThrowsAsync<DeveloperAuthException>(() => f.Service.SaveRedirects(f.Owner, app.AppId, new(["https://example.com/#bad"]), default));
        Assert.Single(await f.Service.Redirects(f.Owner, app.AppId, default));
        await f.Service.Delete(f.Owner, app.AppId, default);
        Assert.NotNull((await f.Db.MiniApps.SingleAsync()).DeletedAt);
        Assert.NotNull((await f.Db.MiniAppOAuthClients.SingleAsync()).RevokedAt);
        Assert.NotNull((await f.Db.MiniAppClientSecrets.SingleAsync()).RevokedAt);
        Assert.Equal(404, (await Assert.ThrowsAsync<DeveloperAuthException>(() => f.Service.Get(f.Owner, app.AppId, default))).Status);
    }

    [Fact]
    public async Task MissingEncryptionKeyAndQuotaLeaveNoPartialApp()
    {
        await using var f = await Fixture.Create();
        f.Options.ActiveKeyVersion = "missing";
        await Assert.ThrowsAsync<DeveloperAuthException>(() => f.App());
        f.Db.ChangeTracker.Clear();
        Assert.Empty(await f.Db.MiniApps.ToListAsync()); Assert.Empty(await f.Db.MiniAppOAuthClients.ToListAsync());
        Assert.Empty(await f.Db.MiniAppClientSecrets.ToListAsync()); Assert.Empty(await f.Db.MiniAppAuditLogs.ToListAsync());
        f.Options.ActiveKeyVersion = "test"; f.Options.MaxAppsPerDeveloper = 1;
        await f.App();
        Assert.Equal("APP_LIMIT_REACHED", (await Assert.ThrowsAsync<DeveloperAuthException>(() => f.App())).Code);
        Assert.Single(await f.Db.MiniApps.ToListAsync());
    }

    [Fact]
    public async Task LogoTicketsAreOwnerBoundSingleUseAndExpire()
    {
        await using var f = await Fixture.Create();
        var ticket = new MiniAppLogoUpload { DeveloperAccountId = f.Owner, PublicId = "test-logo", Url = "https://example.com/test.png", ExpiresAt = DateTime.UtcNow.AddHours(1) };
        f.Db.MiniAppLogoUploads.Add(ticket); await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<DeveloperAuthException>(() => f.Service.Create(Guid.NewGuid(), new("Wrong owner", "other", Guid.NewGuid(), ticket.Id), default));
        f.Db.ChangeTracker.Clear();
        var app = await f.Service.Create(f.Owner, new("Own logo", "other", Guid.NewGuid(), ticket.Id), default);
        Assert.Equal(ticket.Url, app.LogoUrl);
        await Assert.ThrowsAsync<DeveloperAuthException>(() => f.Service.Create(f.Owner, new("Reuse logo", "other", Guid.NewGuid(), ticket.Id), default));
        f.Db.ChangeTracker.Clear();
        var expired = new MiniAppLogoUpload { DeveloperAccountId = f.Owner, PublicId = "expired", Url = ticket.Url, ExpiresAt = DateTime.UtcNow.AddSeconds(-1) };
        f.Db.MiniAppLogoUploads.Add(expired); await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<DeveloperAuthException>(() => f.Service.Update(f.Owner, app.AppId, new(LogoUploadId: expired.Id), default));
        f.Db.ChangeTracker.Clear();
        Assert.Single(await f.Db.MiniApps.ToListAsync());
    }

    [Fact]
    public async Task DatabaseWriteFailureRollsBackCreationAndRotation()
    {
        await using var f = await Fixture.Create();
        await f.Db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_mini_audit BEFORE INSERT ON MiniAppAuditLogs BEGIN SELECT RAISE(ABORT, 'test write failure'); END");
        await Assert.ThrowsAsync<DbUpdateException>(() => f.App());
        f.Db.ChangeTracker.Clear();
        Assert.Empty(await f.Db.MiniApps.ToListAsync()); Assert.Empty(await f.Db.MiniAppOAuthClients.ToListAsync()); Assert.Empty(await f.Db.MiniAppClientSecrets.ToListAsync());
        await f.Db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_mini_audit");
        var app = await f.App(); var original = await f.Service.Credentials(f.Owner, app.AppId, false, default);
        await f.Db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_mini_audit BEFORE INSERT ON MiniAppAuditLogs BEGIN SELECT RAISE(ABORT, 'test write failure'); END");
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Service.Credentials(f.Owner, app.AppId, true, default));
        f.Db.ChangeTracker.Clear(); await f.Db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_mini_audit");
        Assert.Single(await f.Db.MiniAppClientSecrets.ToListAsync());
        Assert.Equal(original.ClientSecret, (await f.Service.Credentials(f.Owner, app.AppId, false, default)).ClientSecret);
    }

    [Fact]
    public void EncryptionRejectsTamperingWrongClientAndMissingKeys()
    {
        var options = TestOptions(); var p = new MiniAppSecretProtector(Options.Create(options)); var client = Guid.NewGuid();
        var encrypted = p.Encrypt("secret-test-value", client);
        Assert.Equal("secret-test-value", p.Decrypt(encrypted.Ciphertext, encrypted.Version, client));
        Assert.Throws<DeveloperAuthException>(() => p.Decrypt(encrypted.Ciphertext, encrypted.Version, Guid.NewGuid()));
        var bytes = Convert.FromBase64String(encrypted.Ciphertext); bytes[^1] ^= 1;
        Assert.Throws<DeveloperAuthException>(() => p.Decrypt(Convert.ToBase64String(bytes), encrypted.Version, client));
        options.EncryptionKeys.Clear(); Assert.Throws<DeveloperAuthException>(() => p.Decrypt(encrypted.Ciphertext, encrypted.Version, client));
    }

    [Theory]
    [InlineData("text/plain", 100)]
    [InlineData("image/png", 100)]
    [InlineData("image/svg+xml", 100)]
    [InlineData("image/jpeg", 2097153)]
    public void InvalidLogoRejected(string mime, int length) => Assert.Throws<DeveloperAuthException>(() => MiniAppLogoStorage.Validate(new byte[length], mime));

    public static MiniAppOptions TestOptions() => new() { ActiveKeyVersion = "test", EncryptionKeys = new() { ["test"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) } };
    private sealed class Environment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "MiniAppTests";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
    private sealed class Fixture(SqliteConnection connection, AppDbContext db, MiniAppOptions options, MiniAppService service, Guid owner) : IAsyncDisposable
    {
        public AppDbContext Db => db; public MiniAppService Service => service; public Guid Owner => owner; public MiniAppOptions Options => options;
        public Task<MiniAppDto> App() => Service.Create(Owner, new("Test app", "utilities", Guid.NewGuid()), default);
        public static async Task<Fixture> Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
            connection.CreateFunction<string?, int>("char_length", s => s?.Length ?? 0);
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var owner = new DeveloperAccount { FullName = "Test Developer", EmailNormalized = "MINI@EXAMPLE.TEST", Status = DeveloperStatus.Active, EmailVerifiedAt = DateTime.UtcNow };
            db.DeveloperAccounts.Add(owner); await db.SaveChangesAsync();
            var o = TestOptions(); var wrapped = Microsoft.Extensions.Options.Options.Create(o);
            return new(connection, db, o, new(db, new(wrapped), wrapped, new Environment()), owner.Id);
        }
        public async ValueTask DisposeAsync() { await db.DisposeAsync(); await connection.DisposeAsync(); }
    }
}
