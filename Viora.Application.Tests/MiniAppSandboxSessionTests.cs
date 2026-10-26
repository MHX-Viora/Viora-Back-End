using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class MiniAppSandboxSessionTests
{
    private sealed class WrappedConflict : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken ct=default)
        {
            if(data.Context!.ChangeTracker.Entries<MiniAppSandboxSession>().Any(x=>x.State==EntityState.Added))
                throw new InvalidOperationException("transient",new DbUpdateException("wrapped",new Npgsql.PostgresException("conflict","ERROR","ERROR","40001")));
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task NestedProviderConflictReturns409AndRollsBackWithoutRetry()
    {
        await using var f=await SandboxFixture.Create(configure:builder=>builder.AddInterceptors(new WrappedConflict()));
        var error=await Assert.ThrowsAsync<DeveloperAuthException>(()=>f.Service().CreateAsync(f.Actor,f.App.AppId,default));
        Assert.Equal(409,error.Status);Assert.Null(error.InnerException);
        Assert.Empty(await f.Db.MiniAppSandboxSessions.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task NonSsoPreviewHasIndependentBoundedSessionAndCloseIsIdempotent()
    {
        await using var fixture=await SandboxFixture.Create(); var service=fixture.Service();
        var session=await service.CreateAsync(fixture.Actor,fixture.App.AppId,default);
        Assert.Equal("Sandbox",session.Environment); Assert.True(session.ExpiresAt<=fixture.Clock.Now.UtcDateTime.AddMinutes(15));
        Assert.False(fixture.App.SsoEnabled); Assert.Empty(fixture.Db.AnktSsoSessions); Assert.Empty(fixture.Db.MiniAppUserConsents);
        var conflict=await Assert.ThrowsAsync<DeveloperAuthException>(()=>service.CreateAsync(fixture.Actor,fixture.App.AppId,default));
        Assert.Equal(409,conflict.Status);
        await service.RevokeAsync(fixture.Actor,fixture.App.AppId,session.Id,default);
        await service.RevokeAsync(fixture.Actor,fixture.App.AppId,session.Id,default);
        Assert.NotNull((await fixture.Db.MiniAppSandboxSessions.AsNoTracking().SingleAsync()).RevokedAt);
        await Assert.ThrowsAsync<DeveloperAuthException>(()=>service.RequireSessionAsync(fixture.Actor,fixture.App.AppId,session.Id,default));
        Assert.NotEqual(session.Id,(await service.CreateAsync(fixture.Actor,fixture.App.AppId,default)).Id);
    }

    [Theory]
    [InlineData("domain")]
    [InlineData("client")]
    [InlineData("source")]
    [InlineData("suspended")]
    [InlineData("configuration")]
    [InlineData("expired")]
    public async Task LiveSessionFailsClosedAfterEligibilityOrSnapshotChanges(string change)
    {
        await using var f=await SandboxFixture.Create(); var service=f.Service(); var session=await service.CreateAsync(f.Actor,f.App.AppId,default);
        switch(change)
        {
            case "domain": f.Proof.RevokedAt=DateTime.UtcNow; break;
            case "client": f.Client.SandboxEnabled=false; break;
            case "source": f.Source.RevokedAt=DateTime.UtcNow; break;
            case "suspended": f.App.Status=MiniAppStatus.Suspended; break;
            case "configuration": f.App.Version=Guid.NewGuid(); break;
            case "expired": f.Clock.Now+=TimeSpan.FromMinutes(16); break;
        }
        await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<DeveloperAuthException>(()=>service.RequireSessionAsync(f.Actor,f.App.AppId,session.Id,default));
    }

    [Fact]
    public async Task AnotherActorCannotReadRevokeOrCreateAgainstTheOwnedApp()
    {
        await using var f=await SandboxFixture.Create(); var service=f.Service(); var session=await service.CreateAsync(f.Actor,f.App.AppId,default);
        var other=f.Actor with {Id=Guid.NewGuid()};
        await Assert.ThrowsAsync<DeveloperAuthException>(()=>service.RequireSessionAsync(other,f.App.AppId,session.Id,default));
        await Assert.ThrowsAsync<DeveloperAuthException>(()=>service.RevokeAsync(other,f.App.AppId,session.Id,default));
        await Assert.ThrowsAsync<DeveloperAuthException>(()=>service.CreateAsync(other,f.App.AppId,default));
        Assert.Null((await f.Db.MiniAppSandboxSessions.SingleAsync()).RevokedAt);
    }
}

internal sealed class SandboxClock : TimeProvider
{
    public DateTimeOffset Now {get;set;}=DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow()=>Now;
}

internal sealed class SandboxFixture : IAsyncDisposable
{
    private SqliteConnection? connection;
    public AppDbContext Db {get;private set;}=null!;
    public SandboxClock Clock {get;}=new();
    public MiniApp App {get;private set;}=null!;
    public MiniAppOAuthClient Client {get;private set;}=null!;
    public MiniAppDomainVerification Proof {get;private set;}=null!;
    public DeveloperSession Source {get;private set;}=null!;
    public SandboxActor Actor=>new(SandboxActorKind.Developer,App.DeveloperAccountId,Source.Id);
    public MiniAppSandboxOptions Options {get;}=new() {Enabled=true,PortalOrigin="https://portal.ankt.example",HostOrigin="https://sandbox.ankt.example",Issuer="https://identity.ankt.example/"};
    public MiniAppSandboxSessionService Service()=>new(Db,new MiniAppSandboxAccess(Db,Clock,Options),Clock,Options);
    public static async Task<SandboxFixture> Create(string? postgres=null,Action<DbContextOptionsBuilder>? configure=null)
    {
        var f=new SandboxFixture(); var options=new DbContextOptionsBuilder<AppDbContext>();
        if(postgres==null)
        {
            f.connection=new("Data Source=:memory:"); await f.connection.OpenAsync(); f.connection.CreateFunction<string?,int>("char_length",v=>v?.Length??0);
            options.UseSqlite(f.connection);
        }
        else options.UseNpgsql(postgres);
        configure?.Invoke(options); f.Db=new(options.Options); await f.Db.Database.EnsureCreatedAsync();
        // Historical migration tests intentionally stop before the Sandbox identity
        // migration, but seed/read Accounts through today's CLR model. Add only its
        // unrelated account marker to that disposable fixture; target migration
        // tables, triggers and up/down assertions remain under the test's control.
        if (postgres != null) await f.Db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Accounts\" ADD COLUMN IF NOT EXISTS \"IsSandbox\" boolean NOT NULL DEFAULT false");
        var owner=new DeveloperAccount {EmailNormalized="sandbox-fixture@example.test",Status=DeveloperStatus.Active};
        f.App=new() {Developer=owner,AppId="app_sandbox_fixture",Name="Fixture",Category="utilities",WebsiteUrl="https://partner.example.com/app",Status=MiniAppStatus.Draft};
        f.Source=new() {Account=owner,TokenHash="sandbox-fixture-source",ExpiresAt=f.Clock.Now.UtcDateTime.AddHours(1),AbsoluteExpiresAt=f.Clock.Now.UtcDateTime.AddHours(2)};
        f.Client=new() {MiniApp=f.App,ClientKey="sandbox-fixture-client",ClientType="Public",SandboxEnabled=true};
        f.Proof=new() {MiniApp=f.App,Hostname="partner.example.com",VerificationFileName="sandbox-fixture.txt",Status=MiniAppDomainStatus.Verified,ExpiresAt=f.Clock.Now.UtcDateTime.AddHours(1)};
        f.Db.DeveloperSessions.Add(f.Source); f.Db.MiniAppOAuthClients.Add(f.Client); f.Db.MiniAppDomainVerifications.Add(f.Proof); await f.Db.SaveChangesAsync();
        return f;
    }
    public async ValueTask DisposeAsync() {await Db.DisposeAsync(); if(connection!=null)await connection.DisposeAsync();}
}
