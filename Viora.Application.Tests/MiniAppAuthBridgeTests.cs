using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Domain.Entities;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;
using viora_BE.Developer;
using Xunit;

public sealed class MiniAppAuthBridgeTests
{
    private const string Client = "app_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", Origin = "https://partner.example.com", Verifier = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQ";
    internal sealed class Host : IAsyncDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        private WebApplication app = null!;
        public HttpClient Http = null!;
        public MiniAppBridgeActor Owner = null!, User = null!;
        public string SocialToken = "";
        public async Task<T> Scope<T>(Func<IServiceProvider,Task<T>> run) { await using var scope=app.Services.CreateAsyncScope();return await run(scope.ServiceProvider); }
        public static async Task<Host> Create(IsolatedPostgres? postgres=null, int partnerRateLimit=6000)
        {
            var h=new Host();await h.connection.OpenAsync();h.connection.CreateFunction<string?,int>("char_length",x=>x?.Length??0);
            var b=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Production"});b.Configuration.Sources.Clear();b.Logging.ClearProviders();
            b.Configuration.AddInMemoryCollection(new Dictionary<string,string?>{
                ["MiniAppAuth:Enabled"]="true",["MiniAppAuth:AllowedHostOrigin"]="https://mxh.ankt.vn",["SandboxTest:Enabled"]="true",
                ["MiniAppAuth:PartnerRequestsPerMinute"]=partnerRateLimit.ToString()});
            b.WebHost.UseTestServer();b.Services.AddDbContext<AppDbContext>(o=>{if(postgres==null)o.UseSqlite(h.connection);else o.UseNpgsql(postgres.ConnectionString);});
            var key=new SymmetricSecurityKey(new byte[64].Select((_,i)=>(byte)(i+1)).ToArray());
            b.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o=>{
                o.MapInboundClaims=false;o.TokenValidationParameters=new(){ValidIssuer="https://social.test",ValidAudience="ankt",IssuerSigningKey=key,ValidAlgorithms=[SecurityAlgorithms.HmacSha256]};});
            b.Services.AddAuthorization();b.Services.AddRateLimiter(o=>o.RejectionStatusCode=429);b.Services.AddMiniAppAuthBridge(b.Configuration);
            h.app=b.Build();if(postgres!=null)await h.Scope(async s=>{await s.GetRequiredService<AppDbContext>().Database.MigrateAsync();return true;});
            await h.Scope(async s=>{
                var db=s.GetRequiredService<AppDbContext>();await db.Database.EnsureCreatedAsync();
                var owner=new DeveloperAccount{FullName="Bridge owner",EmailNormalized="BRIDGE@EXAMPLE.TEST",Status=DeveloperStatus.Active};
                var source=new DeveloperSession{Account=owner,TokenHash="source",ExpiresAt=DateTime.UtcNow.AddHours(1),AbsoluteExpiresAt=DateTime.UtcNow.AddHours(1)};
                var user=new Account{Email="real@example.test",User=new User{DisplayName="Real user"}};var sid=Guid.NewGuid();
                db.AddRange(source,user,new MiniApp{AppId=Client,Name="Bridge",Category="utilities",Developer=owner,WebsiteUrl=Origin+"/app"});
                db.RefreshTokens.Add(new(){Account=user,SessionId=sid,TokenHash="social-source",ExpiresAt=DateTime.UtcNow.AddHours(1)});await db.SaveChangesAsync();
                var app=await db.MiniApps.SingleAsync();db.MiniAppDomainVerifications.Add(new(){MiniAppId=app.Id,Hostname="partner.example.com",Status=MiniAppDomainStatus.Verified,ExpiresAt=DateTime.UtcNow.AddHours(1)});await db.SaveChangesAsync();
                h.Owner=new(owner.Id,source.Id,true);h.User=new(user.Id,sid,false);
                h.SocialToken=new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor{Issuer="https://social.test",Audience="ankt",Expires=DateTime.UtcNow.AddMinutes(5),
                    Subject=new ClaimsIdentity([new("sub",user.Id.ToString()),new("sid",sid.ToString()),new("token_type","access")]),SigningCredentials=new(key,SecurityAlgorithms.HmacSha256)});
                return true;
            });
            h.app.UseRouting();h.app.UseCors();h.app.UseAuthentication();h.app.UseRateLimiter();h.app.UseAuthorization();h.app.MapMiniAppAuthBridge();
            h.app.MapGet("/production",()=>"ok").RequireAuthorization();await h.app.StartAsync();h.Http=h.app.GetTestClient();h.Http.BaseAddress=new("https://api.test");return h;
        }
        public Task<MiniAppBridgeLaunch> Launch(MiniAppBridgeActor? actor=null)=>Scope(s=>s.GetRequiredService<MiniAppBridgeService>().LaunchAsync(actor??Owner,new(Client,MiniAppBridgeService.Challenge(Verifier),Origin),default));
        public Task<MiniAppBridgeToken> Exchange(MiniAppBridgeLaunch launch,string verifier=Verifier,string origin=Origin,string client=Client)=>Scope(s=>s.GetRequiredService<MiniAppBridgeService>().ExchangeAsync(new(client,launch.SessionId,launch.Code,verifier),origin,default));
        public Task<MiniAppBridgeUser> Me(MiniAppBridgeToken token,string origin=Origin)=>Scope(s=>s.GetRequiredService<MiniAppBridgeService>().MeAsync(token.AccessToken,origin,default,Client));
        public async ValueTask DisposeAsync(){Http.Dispose();await app.DisposeAsync();await connection.DisposeAsync();}
    }
    [Fact] public async Task SandboxWithoutOidcUsesRealSharedDatabaseUserAndSeparateSessions()
    {
        await using var h=await Host.Create();var first=await h.Launch();var second=await h.Launch();var a=await h.Exchange(first);var b=await h.Exchange(second);
        Assert.NotEqual(a.AccessToken,b.AccessToken);var user=await h.Me(a);Assert.True(user.IsSandbox);Assert.Equal("ankt_sandbox_test",user.Username);Assert.Equal(user.Id,(await h.Me(b)).Id);
        await h.Scope(async s=>{var db=s.GetRequiredService<AppDbContext>();Assert.Equal(1,await db.Accounts.IgnoreQueryFilters().CountAsync(x=>x.IsSandbox));
            var stored=await db.Set<MiniAppBridgeSession>().AsNoTracking().FirstAsync();Assert.DoesNotContain(first.Code,stored.CodeHash);Assert.DoesNotContain(a.AccessToken,stored.AccessHash!);return true;});
        await h.Scope(async s=>{await s.GetRequiredService<MiniAppBridgeService>().RevokeAsync(first.SessionId,h.Owner,default);return true;});
        await Assert.ThrowsAsync<DeveloperAuthException>(()=>h.Me(a));Assert.Equal(user.Id,(await h.Me(b)).Id);
    }
    [Fact] public async Task DisabledOidcNeedsNoCertificatesAndExposesNoLegacyProtocolEndpoints()
    {
        await using var h=await Host.Create();
        Assert.Equal(HttpStatusCode.NotFound,(await h.Http.GetAsync("/.well-known/openid-configuration")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await h.Http.GetAsync("/connect/authorize")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await h.Http.PostAsJsonAsync("/connect/token",new{})).StatusCode);
        Assert.True((await h.Me(await h.Exchange(await h.Launch()))).IsSandbox);
    }
    [Theory][InlineData("proof")][InlineData("origin")][InlineData("client")][InlineData("expired")][InlineData("source")][InlineData("domain")]
    public async Task ExchangeRejectsInvalidBindings(string change)
    {
        await using var h=await Host.Create();var launch=await h.Launch();
        if(change=="expired")await h.Scope(async s=>{await s.GetRequiredService<AppDbContext>().Set<MiniAppBridgeSession>().ExecuteUpdateAsync(x=>x.SetProperty(r=>r.CodeExpiresAt,DateTime.UtcNow.AddMinutes(-1)));return true;});
        if(change=="source")await h.Scope(async s=>{await s.GetRequiredService<AppDbContext>().DeveloperSessions.ExecuteUpdateAsync(x=>x.SetProperty(r=>r.RevokedAt,DateTime.UtcNow));return true;});
        if(change=="domain")await h.Scope(async s=>{await s.GetRequiredService<AppDbContext>().MiniAppDomainVerifications.ExecuteUpdateAsync(x=>x.SetProperty(r=>r.RevokedAt,DateTime.UtcNow));return true;});
        await Assert.ThrowsAsync<DeveloperAuthException>(()=>h.Exchange(launch,change=="proof"?new string('z',43):Verifier,change=="origin"?"https://evil.test":Origin,change=="client"?"wrong":Client));
    }
    [Fact] public async Task ReplayAndTokenSubstitutionAndAnonymousLaunchAreRejected()
    {
        await using var h=await Host.Create();var launch=await h.Launch();var token=await h.Exchange(launch);
        await Assert.ThrowsAsync<DeveloperAuthException>(()=>h.Exchange(launch));await Assert.ThrowsAsync<DeveloperAuthException>(()=>h.Me(token,"https://evil.test"));
        await Assert.ThrowsAsync<DeveloperAuthException>(()=>h.Scope(s=>s.GetRequiredService<MiniAppBridgeService>().MeAsync(token.AccessToken,Origin,default,"app_"+new string('b',43))));
        h.Http.DefaultRequestHeaders.Authorization=new("Bearer",token.AccessToken);Assert.Equal(HttpStatusCode.Unauthorized,(await h.Http.GetAsync("/production")).StatusCode);
        h.Http.DefaultRequestHeaders.Authorization=new("Bearer",h.SocialToken);Assert.Equal(HttpStatusCode.OK,(await h.Http.GetAsync("/production")).StatusCode);
        h.Http.DefaultRequestHeaders.Add("Origin",Origin);h.Http.DefaultRequestHeaders.Add("X-Ankt-MiniApp-Client",Client);Assert.Equal(HttpStatusCode.Unauthorized,(await h.Http.GetAsync("/api/miniapp/auth/me")).StatusCode);
        h.Http.DefaultRequestHeaders.Authorization=null;h.Http.DefaultRequestHeaders.Remove("Origin");h.Http.DefaultRequestHeaders.Add("Origin","https://mxh.ankt.vn");
        Assert.Equal(HttpStatusCode.Unauthorized,(await h.Http.PostAsJsonAsync("/api/miniapp/auth/launch",new MiniAppBridgeLaunchRequest(Client,MiniAppBridgeService.Challenge(Verifier),Origin))).StatusCode);
        await Assert.ThrowsAsync<DeveloperAuthException>(()=>h.Launch(h.User)); // Unpublished production app.
    }
    [Fact] public async Task PostgreSqlAtomicExchangeHasOnlyOneWinner()
    {
        await using var pg=await IsolatedPostgres.Start();await using var h=await Host.Create(pg);var launch=await h.Launch();
        async Task<bool> Exchange(){try{await h.Exchange(launch);return true;}catch(DeveloperAuthException){return false;}}
        Assert.Equal(1,(await Task.WhenAll(Exchange(),Exchange())).Count(x=>x));
    }
    [Fact] public async Task ApprovedProductionAppUsesRealUserWithoutOidcAndExpiresWithSocialLogout()
    {
        await using var h=await Host.Create();
        await h.Scope(async s=>{
            var db=s.GetRequiredService<AppDbContext>();var app=await db.MiniApps.SingleAsync();
            var admin = new Account { Email = "admin@bridge.test", Role = AccountRole.Admin };
            db.Add(admin);
            var snapshot = MiniAppVersionSnapshotPolicy.Create(new(1, app.Name, app.Category, null, "Bridge fixture", null,
                app.WebsiteUrl, "partner.example.com", Origin + "/privacy", Origin + "/terms", null, false, null, null));
            var version = new MiniAppVersion { MiniAppId = app.Id, CreatedByDeveloperId = app.DeveloperAccountId,
                VersionCode = 1, VersionName = "1.0", SnapshotJson = snapshot.Json, SnapshotHash = snapshot.Hash,
                State = MiniAppVersionState.Submitted, SubmittedAt = DateTime.UtcNow };
            db.Add(version); await db.SaveChangesAsync();
            var review = new MiniAppReviewRequest { MiniAppId = app.Id, MiniAppVersionId = version.Id,
                DeveloperAccountId = app.DeveloperAccountId, Status = MiniAppReviewStatus.PendingReview,
                SubmittedAt = DateTime.UtcNow, EvidenceAttestationJson = "{}", CorrelationId = Guid.NewGuid() };
            db.Add(review); await db.SaveChangesAsync();
            review.Status = MiniAppReviewStatus.InReview; review.AssignedAdminAccountId = admin.Id;
            review.ReviewStartedAt = DateTime.UtcNow; await db.SaveChangesAsync();
            review.Status = MiniAppReviewStatus.Approved; review.DecidedAt = DateTime.UtcNow; await db.SaveChangesAsync();
            db.AddRange(new MiniAppPublication { MiniAppId = app.Id, MiniAppVersionId = version.Id,
                ApprovedReviewRequestId = review.Id, PublishedByDeveloperId = app.DeveloperAccountId, PublishedAt = DateTime.UtcNow },
                new MiniAppOperationalState { MiniAppId = app.Id });
            await db.SaveChangesAsync();
            return true;
        });
        h.Http.DefaultRequestHeaders.Add("Origin","https://mxh.ankt.vn");h.Http.DefaultRequestHeaders.Authorization=new("Bearer",h.SocialToken);
        var result=await h.Http.PostAsJsonAsync("/api/miniapp/auth/launch",new MiniAppBridgeLaunchRequest(Client,MiniAppBridgeService.Challenge(Verifier),Origin));
        Assert.Equal(HttpStatusCode.OK,result.StatusCode);var launch=(await result.Content.ReadFromJsonAsync<MiniAppBridgeLaunch>())!;
        var token=await h.Exchange(launch);Assert.StartsWith("map_",token.AccessToken);var user=await h.Me(token);Assert.False(user.IsSandbox);Assert.Equal("Real user",user.DisplayName);
        // The approved Production bridge also authenticates a partner backend,
        // with a different credential from Sandbox and no ANKT access token.
        var partner = await h.Scope(async s => {
            s.GetRequiredService<IOptions<MiniAppBridgeOptions>>().Value.Partners[Client] = new() {
                ProductionSecretHash = MiniAppBridgeService.Hash(new string('p', 43)),
                SandboxSecretHash = MiniAppBridgeService.Hash(new string('s', 43)) };
            var service = s.GetRequiredService<MiniAppPartnerLoginService>();
            var code = await service.IssueAsync(token.AccessToken, Origin, Client, default);
            await Assert.ThrowsAsync<DeveloperAuthException>(() => service.ExchangeAsync(Client, new string('s', 43), code.Code, default));
            var identity = await service.ExchangeAsync(Client, new string('p', 43), code.Code, default);
            Assert.Equal("Production", identity.Environment); Assert.Equal(user.Id, identity.User.Id);
            return identity;
        });
        await h.Scope(async s=>{await s.GetRequiredService<AppDbContext>().RefreshTokens.ExecuteUpdateAsync(x=>x.SetProperty(r=>r.RevokedAt,DateTime.UtcNow));return true;});
        await Assert.ThrowsAsync<DeveloperAuthException>(() => h.Scope(s => s.GetRequiredService<MiniAppPartnerLoginService>()
            .ValidateAsync(Client, new string('p', 43), partner.SessionId, default)));
        await Assert.ThrowsAsync<DeveloperAuthException>(()=>h.Me(token));
    }
    [Fact] public async Task ScopeEscalationExpiredAccessAndWrongOwnerRevocationFailClosed()
    {
        await using var h=await Host.Create();
        await Assert.ThrowsAsync<DeveloperAuthException>(()=>h.Scope(s=>s.GetRequiredService<MiniAppBridgeService>().LaunchAsync(h.Owner,new(Client,MiniAppBridgeService.Challenge(Verifier),Origin,"email"),default)));
        var launch=await h.Launch();var token=await h.Exchange(launch);
        await Assert.ThrowsAsync<DeveloperAuthException>(()=>h.Scope(async s=>{await s.GetRequiredService<MiniAppBridgeService>().RevokeAsync(launch.SessionId,h.Owner with{Id=Guid.NewGuid()},default);return true;}));
        await h.Scope(async s=>{await s.GetRequiredService<AppDbContext>().Set<MiniAppBridgeSession>().ExecuteUpdateAsync(x=>x.SetProperty(r=>r.AccessExpiresAt,DateTime.UtcNow.AddSeconds(-1)));return true;});
        await Assert.ThrowsAsync<DeveloperAuthException>(()=>h.Me(token));
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("developer-session")]
    [InlineData("suspended")]
    [InlineData("deleted")]
    public async Task SandboxLaunchRejectsUnauthorizedOrUnavailableApp(string change)
    {
        await using var h = await Host.Create();
        if (change == "suspended" || change == "deleted") await h.Scope(async s =>
        {
            var db = s.GetRequiredService<AppDbContext>();
            var app = await db.MiniApps.SingleAsync();
            if (change == "suspended") app.Status = MiniAppStatus.Suspended;
            else app.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(); return true;
        });
        var actor = change == "owner" ? h.Owner with { Id = Guid.NewGuid() }
            : change == "developer-session" ? h.Owner with { SourceSessionId = Guid.NewGuid() } : h.Owner;
        await Assert.ThrowsAsync<DeveloperAuthException>(() => h.Launch(actor));
    }

    [Fact] public async Task EnvironmentPrefixSubstitutionCannotPromoteSandboxToken()
    {
        await using var h = await Host.Create();
        var token = await h.Exchange(await h.Launch());
        await Assert.ThrowsAsync<DeveloperAuthException>(() => h.Me(token with { AccessToken = "map_" + token.AccessToken[4..] }));
        await h.Scope(async s =>
        {
            var db = s.GetRequiredService<AppDbContext>();
            var session = await db.Set<MiniAppBridgeSession>().SingleAsync();
            session.IsSandbox = false; await db.SaveChangesAsync(); return true;
        });
        await Assert.ThrowsAsync<DeveloperAuthException>(() => h.Me(token));
    }

    [Fact] public async Task DeveloperLogoutRevokesExistingSandboxAccess()
    {
        await using var h = await Host.Create();
        var token = await h.Exchange(await h.Launch());
        await h.Scope(async s =>
        {
            await s.GetRequiredService<AppDbContext>().DeveloperSessions.ExecuteUpdateAsync(
                x => x.SetProperty(r => r.RevokedAt, DateTime.UtcNow)); return true;
        });
        await Assert.ThrowsAsync<DeveloperAuthException>(() => h.Me(token));
    }
}
