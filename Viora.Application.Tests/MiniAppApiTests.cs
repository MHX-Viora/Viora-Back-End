using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Viora.Application.Developers;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.Persistence.Migrations;
using Viora.Infrastructure.Developers;
using viora_BE.Developer;
using Xunit;

public sealed class MiniAppApiTests
{
    [Fact]
    public async Task Stage4EndpointsUseRealSessionsOwnerBoundFilesAndFixedVerificationDestination()
    {
        await using var host = await Host.Create(); var c = host.Client;
        var missing = "/api/developer/mini-apps/app_" + new string('x',43);
        foreach(var suffix in new[] {"/integration","/domain-verification","/domain-verification/file","/readiness"})
            Assert.Equal(HttpStatusCode.Unauthorized,(await c.GetAsync(missing+suffix)).StatusCode);
        c.DefaultRequestHeaders.Authorization = new("DeveloperSession",await host.Login("domain-a@example.test"));
        var app = (await (await c.PostAsJsonAsync("/api/developer/mini-apps",new MiniAppCreateRequest("Domain API app","utilities",Guid.NewGuid()))).Content.ReadFromJsonAsync<MiniAppDto>())!;
        var root = "/api/developer/mini-apps/"+app.AppId;
        var request = new MiniAppIntegrationRequest("https://PARTNER.example.com/app","support@example.com","Description","Full description","https://policies.example.com/privacy","https://policies.example.com/terms");
        var saved=await c.PutAsJsonAsync(root+"/integration",request); Assert.Equal(HttpStatusCode.OK,saved.StatusCode);
        Assert.Equal("https://partner.example.com/app",(await saved.Content.ReadFromJsonAsync<MiniAppIntegrationDto>())!.WebsiteUrl);
        var download=await c.GetAsync(root+"/domain-verification/file"); Assert.Equal(HttpStatusCode.OK,download.StatusCode);
        Assert.Equal("text/plain",download.Content.Headers.ContentType!.MediaType); Assert.Equal("attachment",download.Content.Headers.ContentDisposition!.DispositionType); Assert.Equal("no-store",download.Headers.CacheControl!.ToString());
        var proof=await download.Content.ReadAsStringAsync(); Assert.Contains("app_id="+app.AppId,proof); Assert.DoesNotContain("cs_",proof);
        host.Fetcher.Body=proof;
        var verify=await c.PostAsJsonAsync(root+"/domain-verification/verify",new {url="https://127.0.0.1/metadata",status="Verified",verifiedAt=DateTime.UtcNow});
        Assert.Equal(HttpStatusCode.OK,verify.StatusCode); Assert.Equal("Verified",(await verify.Content.ReadFromJsonAsync<MiniAppDomainDto>())!.Status);
        Assert.Equal("partner.example.com",host.Fetcher.LastVerificationUri!.Host); Assert.StartsWith("/ankt-verify-",host.Fetcher.LastVerificationUri.AbsolutePath);
        var read=await c.GetAsync(root+"/domain-verification"); Assert.Equal("Verified",(await read.Content.ReadFromJsonAsync<MiniAppDomainDto>())!.Status);
        var readiness=await c.GetAsync(root+"/readiness"); Assert.Equal(HttpStatusCode.OK,readiness.StatusCode); Assert.False((await readiness.Content.ReadFromJsonAsync<MiniAppReadinessDto>())!.ReadyForSandbox);
        var changed=await c.PutAsJsonAsync(root+"/integration",request with {WebsiteUrl="https://other.example.com"}); Assert.Equal(HttpStatusCode.OK,changed.StatusCode);
        Assert.Equal("Pending",(await (await c.GetAsync(root+"/domain-verification")).Content.ReadFromJsonAsync<MiniAppDomainDto>())!.Status);
        c.DefaultRequestHeaders.Authorization = new("DeveloperSession",await host.Login("domain-b@example.test"));
        foreach(var suffix in new[] {"/integration","/domain-verification","/domain-verification/file","/readiness"}) Assert.Equal(HttpStatusCode.NotFound,(await c.GetAsync(root+suffix)).StatusCode);
        foreach(var suffix in new[]{"/domain-verification/verify","/domain-verification/regenerate"}) Assert.Equal(HttpStatusCode.NotFound,(await c.PostAsJsonAsync(root+suffix,new {})).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await c.PutAsJsonAsync(root+"/integration",request)).StatusCode);
    }

    [Fact]
    public void Stage4MigrationOnlyAddsProofTableAndSsoFlagWithReversibleDown()
    {
        var migration=new AddMiniAppDomainVerification();
        var table=Assert.Single(migration.UpOperations.OfType<CreateTableOperation>()); Assert.Equal("MiniAppDomainVerifications",table.Name);
        Assert.All(table.ForeignKeys,fk=>Assert.Equal(ReferentialAction.Restrict,fk.OnDelete));
        var column=Assert.Single(migration.UpOperations.OfType<AddColumnOperation>()); Assert.Equal("SsoEnabled",column.Name); Assert.Equal("MiniApps",column.Table);
        Assert.All(migration.UpOperations,op=>Assert.True(op is CreateTableOperation or CreateIndexOperation or AddColumnOperation));
        Assert.Equal(2,migration.DownOperations.Count); Assert.Single(migration.DownOperations.OfType<DropTableOperation>()); Assert.Single(migration.DownOperations.OfType<DropColumnOperation>());
    }
    [Fact]
    public async Task RealSessionCrudRotationAndAuthorization()
    {
        await using var host = await Host.Create(); var c = host.Client;
        var id = "app_" + new string('x', 43);
        foreach (var path in new[] { "", "/" + id, $"/{id}/activity" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/developer/mini-apps" + path)).StatusCode);
        c.DefaultRequestHeaders.Authorization = new("Bearer", "not-a-developer-session");
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/developer/mini-apps", new { })).StatusCode);
        var tokenA = await host.Login("a@example.test");
        c.DefaultRequestHeaders.Authorization = new("DeveloperSession", tokenA);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/developer/mini-apps", new { name = "", category = "other", requestId = Guid.NewGuid() })).StatusCode);
        var request = new MiniAppCreateRequest("API app", "education", Guid.NewGuid());
        var response = await c.PostAsJsonAsync("/api/developer/mini-apps", request); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var app = (await response.Content.ReadFromJsonAsync<MiniAppDto>())!; var root = "/api/developer/mini-apps/" + app.AppId;
        var repeated = await c.PostAsJsonAsync("/api/developer/mini-apps", request);
        Assert.Equal(app.AppId, (await repeated.Content.ReadFromJsonAsync<MiniAppDto>())!.AppId);
        foreach (var suffix in new[] { "/sso", "/credentials", "/redirect-uris" })
            Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync(root + suffix)).StatusCode);
        var patch = await c.PatchAsJsonAsync(root, new { name = "Persisted", status = "Approved", appId = "hijacked", clientKey = "hijacked" });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode); var saved = (await patch.Content.ReadFromJsonAsync<MiniAppDto>())!;
        Assert.Equal("Persisted", saved.Name); Assert.NotEqual("Approved", saved.Status); Assert.Equal(app.AppId, saved.AppId);
        using var image = new ByteArrayContent(new byte[30]); image.Headers.ContentType = new("image/png");
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync("/api/developer/mini-apps/logo", image)).StatusCode);
        var tokenB = await host.Login("b@example.test"); c.DefaultRequestHeaders.Authorization = new("DeveloperSession", tokenB);
        foreach (var path in new[] { "", "/activity" }) Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync(root + path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PatchAsJsonAsync(root, new { name = "Attacker" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsJsonAsync(root + "/credentials/rotate", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync(root)).StatusCode);
        c.DefaultRequestHeaders.Authorization = new("DeveloperSession", tokenA);
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync(root)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync(root)).StatusCode);
    }

    [Fact]
    public void MigrationIsAdditiveAndRollbackOnlyDropsItsSixTables()
    {
        var migration = new AddDeveloperMiniAppManagement();
        var allowed = new[] { "MiniApps", "MiniAppOAuthClients", "MiniAppClientSecrets", "MiniAppRedirectUris", "MiniAppAuditLogs", "MiniAppLogoUploads" };
        Assert.Equal(6, migration.UpOperations.OfType<CreateTableOperation>().Count());
        Assert.All(migration.UpOperations, op => Assert.True(op is CreateTableOperation or CreateIndexOperation));
        Assert.All(migration.UpOperations.OfType<CreateTableOperation>(), table => {
            Assert.Contains(table.Name, allowed); Assert.All(table.ForeignKeys, fk => Assert.Equal(ReferentialAction.Restrict, fk.OnDelete));
        });
        Assert.Equal(6, migration.DownOperations.Count);
        Assert.All(migration.DownOperations, op => Assert.Contains(Assert.IsType<DropTableOperation>(op).Name, allowed));
    }

    internal sealed class Mail : IDeveloperEmailSender
    {
        public bool IsConfigured => true; public string Token = "";
        public Task SendAsync(string recipient, string subject, string text, CancellationToken ct) { Token = text.Split("#token=")[1].Split('\n')[0].Trim(); return Task.CompletedTask; }
    }
    internal sealed class Host(WebApplication app, SqliteConnection db, HttpClient client, Mail mail, MiniAppIntegrationTests.Fetcher fetcher, SecurityKey adminKey) : IAsyncDisposable
    {
        public HttpClient Client => client;
        public HttpClient RuntimeClient()=>app.GetTestClient();
        public string AdminToken(Guid id,Guid source)=>new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor {
            Issuer="https://sandbox-test-identity.example",Audience="sandbox-admin-test",Expires=DateTime.UtcNow.AddMinutes(15),
            SigningCredentials=new(adminKey,SecurityAlgorithms.HmacSha256),Subject=new ClaimsIdentity(new[]{
                new Claim("sub",id.ToString()),new Claim("sid",source.ToString()),new Claim("role","2"),new Claim("token_type","access")})});
        public MiniAppIntegrationTests.Fetcher Fetcher => fetcher;
        public async Task<string> Login(string email)
        {
            const string password = "Mini App API test password 2026!";
            Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/developer/auth/register", new DeveloperRegisterRequest("Test Developer", email, password, password, true))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/developer/auth/verify-email", new { token = mail.Token })).StatusCode);
            var response = await client.PostAsJsonAsync("/api/developer/auth/login", new DeveloperLoginRequest(email, password));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<DeveloperLoginResult>())!.SessionToken;
        }
        public async Task<T> WithServices<T>(Func<IServiceProvider,Task<T>> action)
        { await using var scope=app.Services.CreateAsyncScope(); return await action(scope.ServiceProvider); }
        public static async Task<Host> Create(bool sandbox=false, IPublicDnsResolver? dns=null, bool review=false, int? reviewReadLimit=null)
        {
            var connection = new SqliteConnection(sandbox?$"Data Source=sandbox-api-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Pooling=False":"Data Source=:memory:");
            await connection.OpenAsync(); connection.CreateFunction<string?, int>("char_length", s => s?.Length ?? 0);
            var b = WebApplication.CreateBuilder(); b.WebHost.UseTestServer(); b.Logging.ClearProviders();
            var adminKey=new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));
            if(sandbox)b.Services.AddAuthentication().AddJwtBearer(options=>{options.MapInboundClaims=false;options.TokenValidationParameters=new(){
                ValidIssuer="https://sandbox-test-identity.example",ValidAudience="sandbox-admin-test",IssuerSigningKey=adminKey,RoleClaimType="role",ValidAlgorithms=[SecurityAlgorithms.HmacSha256]};});
            b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
                ["DeveloperAuth:PortalUrl"] = "https://mxh.ankt.vn/developers", ["MiniApps:ActiveKeyVersion"] = "test",
                ["MiniApps:EncryptionKeys:test"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            });
            if(sandbox) b.Configuration.AddInMemoryCollection(new Dictionary<string,string?> {
                ["MiniAppSandbox:Enabled"]="true",["MiniAppSandbox:PortalOrigin"]="https://portal.ankt.example",
                ["MiniAppSandbox:HostOrigin"]="https://sandbox.ankt.example",["MiniAppSandbox:Issuer"]="https://identity.ankt.example/"});
            if(review)b.Configuration.AddInMemoryCollection(new Dictionary<string,string?> { ["MiniAppReview:Enabled"]="true" });
            if(reviewReadLimit is {} readLimit)b.Configuration.AddInMemoryCollection(new Dictionary<string,string?> { ["MiniAppReview:ReadLimitPerMinute"]=readLimit.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            b.Services.AddDbContext<AppDbContext>(o =>
            {
                if(!sandbox){o.UseSqlite(connection);return;}
                // Keep the isolated memory DB alive, but HTTP/worker scopes own separate transactions.
                var scoped=new SqliteConnection(connection.ConnectionString);scoped.CreateFunction<string?,int>("char_length",s=>s?.Length??0);
                o.UseSqlite(scoped,contextOwnsConnection:true);
            });
            b.Services.AddDeveloperAccounts(b.Configuration); b.Services.AddDeveloperMiniApps(b.Configuration);
            if(dns!=null)b.Services.AddSingleton(dns);
            var fetcher=new MiniAppIntegrationTests.Fetcher(); b.Services.AddSingleton<IPublicWebFetcher>(fetcher);
            var mail = new Mail(); b.Services.AddSingleton<IDeveloperEmailSender>(mail); var app = b.Build();
            using (var scope = app.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();
            MiniAppSandboxRuntimeApi.UseBoundary(app);
            app.UseDeveloperAccountBoundary(); app.UseAuthentication(); app.UseRateLimiter(); app.UseAuthorization(); app.MapDeveloperAccountEndpoints(); app.MapDeveloperMiniApps();
            await app.StartAsync(); return new(app, connection, app.GetTestClient(), mail, fetcher,adminKey);
        }
        public async ValueTask DisposeAsync() { client.Dispose(); await app.DisposeAsync(); await db.DisposeAsync(); }
    }
}
