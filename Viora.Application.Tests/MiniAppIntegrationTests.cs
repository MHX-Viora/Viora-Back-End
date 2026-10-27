using System.Text;
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

public sealed class MiniAppIntegrationTests
{
    [Fact]
    public async Task RegenerationPreservesHistoryAndDatabaseAllowsOnlyOneActiveChallengePerHostname()
    {
        await using var f = await Fixture.Create(); await f.Save();
        var old = await f.Service.File(f.Owner,f.AppId,default); var regenerated=await f.Service.Regenerate(f.Owner,f.AppId,default);
        Assert.NotEqual(old.FileName,regenerated.FileName); Assert.Equal("Pending",regenerated.Status);
        Assert.Equal(2,await f.Db.MiniAppDomainVerifications.CountAsync());
        Assert.Single(await f.Db.MiniAppDomainVerifications.Where(x=>x.RevokedAt==null).ToListAsync());
        var appId=(await f.Db.MiniApps.SingleAsync()).Id;
        var hostname=await f.Db.MiniAppDomainVerifications.Where(x=>x.RevokedAt==null).Select(x=>x.Hostname).SingleAsync();
        f.Db.MiniAppDomainVerifications.Add(new(){ MiniAppId=appId,Hostname=hostname,VerificationFileName="ankt-verify-other.txt",ExpiresAt=DateTime.UtcNow.AddDays(1),VerificationTokenHash=new string('a',64),VerificationMaterialEncrypted="test",SecretKeyVersion="test" });
        await Assert.ThrowsAsync<DbUpdateException>(()=>f.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task ReadinessCannotReturnAConfigurationSnapshotChangedDuringPolicyChecks()
    {
        await using var f=await Fixture.Create(); await f.Save(); var changed=false;
        f.Fetcher.DuringFetch=async()=>{
            if(changed)return; changed=true;
            await using var other=f.OtherDb(); await f.AppsWith(other).Update(f.Owner,f.AppId,new(WebsiteUrl:"https://other.example.com"),default);
        };
        Assert.Equal("CONFIGURATION_CHANGED",(await Assert.ThrowsAsync<DeveloperAuthException>(()=>f.Service.Readiness(f.Owner,f.AppId,default))).Code);
    }

    [Fact]
    public async Task RepeatableFileAndControlledRealHttpsVerificationPersist()
    {
        await using var f = await Fixture.Create(); await f.Save();
        var first = await f.Service.File(f.Owner, f.AppId, default); var repeated = await f.Service.File(f.Owner, f.AppId, default);
        Assert.Equal(first, repeated); Assert.StartsWith("ankt-verify-", first.FileName);
        Assert.DoesNotContain("cs_", first.Content); Assert.Contains("app_id=" + f.AppId, first.Content); Assert.Contains("domain=partner.example.com", first.Content);
        var stored = await f.Db.MiniAppDomainVerifications.SingleAsync(); Assert.DoesNotContain(first.Content.Split("token=")[1].Trim(), stored.VerificationMaterialEncrypted);
        await using var https = new ControlledHttpsSite(first.Content);
        var resolver = new PublicWebFetcherTests.Resolver(); var connector = new PublicWebFetcherTests.Connector(https.Port);
        var service = f.ServiceWith(PublicWebFetcherTests.Create(https, resolver, connector));
        var verified = await service.Verify(f.Owner, f.AppId, default);
        Assert.Equal("Verified", verified.Status); Assert.NotNull(verified.VerifiedAt); Assert.Equal(1, resolver.Calls);
        Assert.Contains("GET /" + first.FileName, https.Request);
        f.Db.ChangeTracker.Clear(); Assert.Equal("Verified", (await f.Service.Domain(f.Owner, f.AppId, default)).Status);
        Assert.All(await f.Db.MiniAppAuditLogs.ToListAsync(), a => Assert.DoesNotContain("token=", a.Metadata));
    }
    [Theory]
    [InlineData("token")][InlineData("app")][InlineData("host")]
    public async Task WrongProofDoesNotVerify(string wrong)
    {
        await using var f = await Fixture.Create(); await f.Save(); var file = await f.Service.File(f.Owner, f.AppId, default);
        var bad = wrong switch { "token" => file.Content.Replace("token=", "token=x"), "app" => file.Content.Replace(f.AppId, "app_wrong"), _ => file.Content.Replace("partner.example.com", "other.example.com") };
        f.Fetcher.Body = bad;
        var result = await f.Service.Verify(f.Owner, f.AppId, default);
        Assert.Equal("Failed", result.Status); Assert.Equal("PROOF_MISMATCH", result.ErrorCode);
    }
    [Theory]
    [InlineData("FILE_NOT_FOUND")][InlineData("TLS_ERROR")][InlineData("FETCH_TIMEOUT")][InlineData("REDIRECT_REJECTED")][InlineData("RESPONSE_TOO_LARGE")][InlineData("DOMAIN_BLOCKED")]
    public async Task FailedRecheckImmediatelyInvalidatesProof(string code)
    {
        await using var f = await Fixture.Create(); await f.Save(); f.Fetcher.Body = (await f.Service.File(f.Owner, f.AppId, default)).Content;
        await f.Service.Verify(f.Owner, f.AppId, default); f.Fetcher.Error = code;
        var result = await f.Service.Verify(f.Owner, f.AppId, default); Assert.Equal("Failed", result.Status); Assert.Equal(code, result.ErrorCode);
        Assert.Equal("failed", (await f.Service.Readiness(f.Owner, f.AppId, default)).Checks.Single(x => x.Key == "domain").Status);
    }
    [Fact]
    public async Task HostChangeThroughLegacyPatchRevokesHistoryButPathOnlyKeepsProof()
    {
        await using var f = await Fixture.Create(); await f.Save(); f.Fetcher.Body = (await f.Service.File(f.Owner, f.AppId, default)).Content;
        await f.Service.Verify(f.Owner, f.AppId, default);
        await f.Apps.Update(f.Owner, f.AppId, new(WebsiteUrl: "https://PARTNER.example.com/another"), default);
        Assert.Equal("Verified", (await f.Service.Domain(f.Owner, f.AppId, default)).Status);
        await f.Apps.Update(f.Owner, f.AppId, new(WebsiteUrl: "https://other.example.com/path"), default);
        f.Db.ChangeTracker.Clear();
        var current = await f.Service.Domain(f.Owner, f.AppId, default); Assert.Equal("Pending", current.Status); Assert.Equal("other.example.com", current.Hostname);
        Assert.Equal(2, await f.Db.MiniAppDomainVerifications.CountAsync()); Assert.Single(await f.Db.MiniAppDomainVerifications.Where(x => x.RevokedAt != null).ToListAsync());
        await f.Apps.Update(f.Owner, f.AppId, new(WebsiteUrl: ""), default); f.Db.ChangeTracker.Clear();
        Assert.Null((await f.Service.Domain(f.Owner, f.AppId, default)).Hostname);
    }
    [Fact]
    public async Task ReadinessRequiresPoliciesAndOnlyRequiresCallbacksWhenSsoEnabled()
    {
        await using var f = await Fixture.Create(); await f.Save(); f.Fetcher.Body = (await f.Service.File(f.Owner, f.AppId, default)).Content;
        await f.Service.Verify(f.Owner, f.AppId, default);
        var ready = await f.Service.Readiness(f.Owner, f.AppId, default);
        Assert.True(ready.ConfigurationReady); Assert.False(ready.ReadyForSandbox); Assert.False(ready.ReadyForReview); Assert.False(ready.SandboxAvailable);
        Assert.DoesNotContain(ready.Checks, x => x.Key == "callbacks");
        f.Fetcher.PolicyError = "FILE_NOT_FOUND";
        Assert.False((await f.Service.Readiness(f.Owner, f.AppId, default)).ConfigurationReady);
        f.Clock.Offset = TimeSpan.FromDays(31);
        Assert.Equal("Expired", (await f.Service.Domain(f.Owner, f.AppId, default)).Status);
        Assert.Equal("failed", (await f.Service.Readiness(f.Owner, f.AppId, default)).Checks.Single(x => x.Key == "domain").Status);
    }
    [Fact]
    public async Task ChallengeExpiryDuringFetchCannotBecomeVerified()
    {
        await using var f = await Fixture.Create(); await f.Save(); f.Fetcher.Body = (await f.Service.File(f.Owner, f.AppId, default)).Content;
        f.Fetcher.DuringFetch = () => { f.Clock.Offset = TimeSpan.FromDays(8); return Task.CompletedTask; };
        Assert.Equal("CHALLENGE_EXPIRED", (await Assert.ThrowsAsync<DeveloperAuthException>(() => f.Service.Verify(f.Owner, f.AppId, default))).Code);
        Assert.Equal("Expired", (await f.Service.Domain(f.Owner, f.AppId, default)).Status);
    }
    [Theory]
    [InlineData("regenerate")][InlineData("website")][InlineData("delete")]
    public async Task ConcurrentChangeCannotCommitStaleVerification(string change)
    {
        await using var f = await Fixture.Create(); await f.Save(); f.Fetcher.Body = (await f.Service.File(f.Owner, f.AppId, default)).Content;
        f.Fetcher.DuringFetch = async () => {
            await using var other = f.OtherDb();
            if (change == "regenerate") await f.ServiceWith(f.Fetcher, other).Regenerate(f.Owner, f.AppId, default);
            else if (change == "website") await f.AppsWith(other).Update(f.Owner, f.AppId, new(WebsiteUrl: "https://other.example.com"), default);
            else await f.AppsWith(other).Delete(f.Owner, f.AppId, default);
        };
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => f.Service.Verify(f.Owner, f.AppId, default));
        f.Db.ChangeTracker.Clear();
        Assert.Empty(await f.Db.MiniAppDomainVerifications.Where(x => x.Status == MiniAppDomainStatus.Verified && x.RevokedAt == null).ToListAsync());
    }
    [Fact]
    public async Task OtherDeveloperCannotUseAnyIntegrationOperationOrDownloadToken()
    {
        await using var f = await Fixture.Create(); await f.Save(); var other = Guid.NewGuid();
        Func<Task>[] calls = [async () => { await f.Service.Get(other, f.AppId, default); }, async () => { await f.Service.Save(other, f.AppId, f.Request, default); },
            async () => { await f.Service.Domain(other, f.AppId, default); }, async () => { await f.Service.File(other, f.AppId, default); },
            async () => { await f.Service.Verify(other, f.AppId, default); }, async () => { await f.Service.Regenerate(other, f.AppId, default); }, async () => { await f.Service.Readiness(other, f.AppId, default); }];
        foreach (var call in calls) Assert.Equal(404, (await Assert.ThrowsAsync<DeveloperAuthException>(call)).Status);
        Assert.Equal(0, f.Fetcher.Calls);
    }
    internal sealed class Fetcher : IPublicWebFetcher
    {
        public string Body = ""; public string? Error; public string? PolicyError; public int Calls; public Uri? LastVerificationUri; public Func<Task>? DuringFetch;
        public async Task<PublicFetchResult> FetchAsync(Uri uri, int maxBytes, bool verificationFile, CancellationToken ct) {
            Calls++; if(verificationFile) LastVerificationUri=uri; if (DuringFetch is not null) await DuringFetch();
            var error = verificationFile ? Error : PolicyError; if (error is not null) throw new PublicFetchException(error, "controlled test error");
            return new(Encoding.UTF8.GetBytes(verificationFile ? Body : "Public policy"), verificationFile ? "text/plain" : "text/html");
        }
    }
    private sealed class Clock : TimeProvider { public TimeSpan Offset; public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + Offset; }
    private sealed class Environment : IHostEnvironment {
        public string EnvironmentName { get; set; } = "Production"; public string ApplicationName { get; set; } = "IntegrationTests";
        public string ContentRootPath { get; set; } = ""; public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
    private sealed class Fixture(SqliteConnection connection, AppDbContext db, MiniAppSecretProtector protector, IOptions<MiniAppOptions> options, Guid owner, string appId) : IAsyncDisposable
    {
        public AppDbContext Db => db; public Guid Owner => owner; public string AppId => appId;
        public readonly Fetcher Fetcher = new(); public readonly Clock Clock = new();
        public MiniAppIntegrationRequest Request = new("https://partner.example.com/app", "support@example.com", "Short description", "Full description", "https://policies.example.com/privacy", "https://policies.example.com/terms");
        public MiniAppService Apps => AppsWith(db);
        public MiniAppService AppsWith(AppDbContext context) => new(context, protector, options, new Environment());
        public MiniAppIntegrationService Service => ServiceWith(Fetcher);
        public MiniAppIntegrationService ServiceWith(IPublicWebFetcher fetcher, AppDbContext? context = null) => new(context ?? db, protector, fetcher, new Environment(), Clock);
        public Task<MiniAppIntegrationDto> Save() => Service.Save(owner, appId, Request, default);
        public AppDbContext OtherDb() => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        public static async Task<Fixture> Create() {
            var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync(); connection.CreateFunction<string?, int>("char_length", x => x?.Length ?? 0);
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options); await db.Database.EnsureCreatedAsync();
            var owner = new DeveloperAccount { FullName = "Domain Developer", EmailNormalized = "DOMAIN@EXAMPLE.COM", Status = DeveloperStatus.Active, EmailVerifiedAt = DateTime.UtcNow };
            db.DeveloperAccounts.Add(owner); await db.SaveChangesAsync(); var options = Options.Create(MiniAppServiceTests.TestOptions()); var protector = new MiniAppSecretProtector(options);
            var app = await new MiniAppService(db, protector, options, new Environment()).Create(owner.Id, new("Domain app", "other", Guid.NewGuid()), default);
            var entity = await db.MiniApps.SingleAsync(); entity.LogoUrl = "https://example.com/logo.png"; await db.SaveChangesAsync();
            return new(connection, db, protector, options, owner.Id, app.AppId);
        }
        public async ValueTask DisposeAsync() { await db.DisposeAsync(); await connection.DisposeAsync(); }
    }
}
