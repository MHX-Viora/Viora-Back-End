using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Xunit;

public sealed class MiniAppSandboxConfigurationTests
{
    [Fact]
    public async Task ConfigurationUsesOnlyItsWebsiteProofWhenOtherHostsAreVerified()
    {
        await using var f = await SandboxFixture.Create(); await CompleteMetadata(f);
        f.Db.Add(MiniAppVersionDomainTests.Proof(f.App.Id, "other.example.com", "configuration-other-host.txt")); await f.Db.SaveChangesAsync();
        var service = Service(f, new Fetcher());
        Assert.True((await service.ReadinessAsync(f.Actor, f.App.AppId, default)).ConfigurationReady);
        f.Proof.RevokedAt = f.Clock.Now.UtcDateTime; f.Proof.Status = MiniAppDomainStatus.Revoked; await f.Db.SaveChangesAsync();
        Assert.False((await service.ReadinessAsync(f.Actor, f.App.AppId, default)).ConfigurationReady);
    }
    private sealed class Fetcher : IPublicWebFetcher, IPublicJsonFetcher
    {
        public List<string> Calls {get;}=[];
        public string? Error {get;set;}
        public Func<Task>? DuringFetch {get;set;}
        public string Issuer {get;set;}="https://identity.ankt.com/";
        public string? DiscoveryOverride {get;set;}
        public string? JwksOverride {get;set;}
        public string? JwksUriOverride {get;set;}
        public string Pkce {get;set;}="S256";
        public async Task<PublicFetchResult> FetchAsync(Uri uri,int maxBytes,bool verificationFile,CancellationToken ct)
        {
            Calls.Add(uri.AbsoluteUri); if(DuringFetch!=null)await DuringFetch();
            if(Error!=null)throw new PublicFetchException(Error,"private upstream details must not escape");
            return new(Encoding.UTF8.GetBytes("page"),"text/html");
        }
        public Task<PublicFetchResult> FetchJsonAsync(Uri uri,int maxBytes,CancellationToken ct)
        {
            Calls.Add(uri.AbsoluteUri);
            if(Error!=null)throw new PublicFetchException(Error,"private upstream details must not escape");
            using var rsa=RSA.Create(2048);var p=rsa.ExportParameters(false);
            static string B64(byte[] b)=>Convert.ToBase64String(b).TrimEnd('=').Replace('+','-').Replace('/','_');
            var json=uri.AbsolutePath.EndsWith("openid-configuration",StringComparison.Ordinal)?DiscoveryOverride??JsonSerializer.Serialize(new {
                issuer=Issuer,authorization_endpoint=Issuer+"connect/authorize",token_endpoint=Issuer+"connect/token",userinfo_endpoint=Issuer+"connect/userinfo",jwks_uri=JwksUriOverride??Issuer+".well-known/jwks.json",
                response_types_supported=new[]{"code"},grant_types_supported=new[]{"authorization_code"},code_challenge_methods_supported=new[]{Pkce},id_token_signing_alg_values_supported=new[]{"RS256"}}):
                JwksOverride??JsonSerializer.Serialize(new {keys=new[]{new {kty="RSA",kid="fixture",use="sig",alg="RS256",n=B64(p.Modulus!),e=B64(p.Exponent!)}}});
            return Task.FromResult(new PublicFetchResult(Encoding.UTF8.GetBytes(json),"application/json"));
        }
    }
    private static MiniAppSandboxConfigurationService Service(SandboxFixture f,Fetcher fetcher)
    {
        f.Options.Issuer="https://identity.ankt.com/";
        return new(f.Db,new(f.Db,f.Clock,f.Options),fetcher,fetcher,f.Options,f.Clock);
    }
    private static async Task CompleteMetadata(SandboxFixture f)
    {
        f.App.LogoUrl="https://assets.partner.com/logo.png";f.App.ShortDescription="Short";f.App.Description="Details";
        f.App.SupportEmail="help@partner.com";f.App.PrivacyPolicyUrl="https://partner.example.com/privacy";f.App.TermsOfServiceUrl="https://partner.example.com/terms";
        await f.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task NonSsoConfigurationCanLaunchButCannotManufactureReviewEvidence()
    {
        await using var f=await SandboxFixture.Create();await CompleteMetadata(f);var fetcher=new Fetcher();
        var readiness=await Service(f,fetcher).ReadinessAsync(f.Actor,f.App.AppId,default);
        Assert.True(readiness.ReadyForSandbox);Assert.True(readiness.ConfigurationReady);Assert.False(readiness.ReadyForReview);
        Assert.Equal(18,readiness.Checks.Length);Assert.Equal(64,readiness.ConfigurationFingerprint.Length);
        foreach(var key in new[]{"callbacks","ssoClient","discovery","consent","token","userInfo"})
        {var check=Assert.Single(readiness.Checks,x=>x.Key==key);Assert.False(check.Applicable);Assert.Equal("Skipped",check.Status);}
        foreach(var key in new[]{"runtime","bridge"})Assert.Contains(key,readiness.BlockingIssues);
        Assert.All(readiness.Checks.Where(x=>x.Status=="Passed"),x=>{Assert.Equal("Server",x.EvidenceSource);Assert.NotNull(x.EvidenceAt);});
        Assert.DoesNotContain(fetcher.Calls,x=>x.Contains("openid-configuration",StringComparison.Ordinal));
        var session=await f.Service().CreateAsync(f.Actor,f.App.AppId,default);
        Assert.Equal(session.ConfigurationFingerprint,readiness.ConfigurationFingerprint);
    }

    [Theory]
    [InlineData("metadata")][InlineData("legal")][InlineData("domain")][InlineData("client")][InlineData("environment")]
    public async Task FailedConfigurationGatesAreReportedWithoutLaunching(string change)
    {
        await using var f=await SandboxFixture.Create();await CompleteMetadata(f);var fetcher=new Fetcher();
        switch(change){case "metadata":f.App.SupportEmail="bad";break;case "legal":fetcher.Error="REDIRECT_REJECTED";break;case "domain":f.Proof.ExpiresAt=f.Clock.Now.UtcDateTime;break;case "client":f.Client.SandboxEnabled=false;break;case "environment":f.Client.Environment=MiniAppOAuthEnvironment.Production;break;}
        await f.Db.SaveChangesAsync();var result=await Service(f,fetcher).ReadinessAsync(f.Actor,f.App.AppId,default);
        Assert.False(result.ConfigurationReady);Assert.False(result.ReadyForReview);
        Assert.Equal(change=="metadata",result.ReadyForSandbox);
        Assert.DoesNotContain("private upstream",JsonSerializer.Serialize(result),StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("snapshot")][InlineData("actor")][InlineData("expiry")]
    public async Task NetworkWorkCannotPublishAChangedOrExpiredSnapshot(string change)
    {
        await using var f=await SandboxFixture.Create();await CompleteMetadata(f);var fetcher=new Fetcher();var called=false;
        fetcher.DuringFetch=async()=>{if(called)return;called=true;
            if(change=="snapshot")f.App.Version=Guid.NewGuid();else if(change=="actor")f.Source.RevokedAt=f.Clock.Now.UtcDateTime;else f.Clock.Now+=TimeSpan.FromHours(1);
            await f.Db.SaveChangesAsync();};
        await Assert.ThrowsAsync<DeveloperAuthException>(()=>Service(f,fetcher).ReadinessAsync(f.Actor,f.App.AppId,default));
    }

    [Fact]
    public async Task ForeignOwnerGetsNoChecksOrNetworkRequests()
    {
        await using var f=await SandboxFixture.Create();var fetcher=new Fetcher();
        var error=await Assert.ThrowsAsync<DeveloperAuthException>(()=>Service(f,fetcher).ReadinessAsync(f.Actor with {Id=Guid.NewGuid()},f.App.AppId,default));
        Assert.Equal(404,error.Status);Assert.Empty(fetcher.Calls);
    }

    [Fact]
    public async Task RealCheckerPersistsOnlyServerConfigurationEvidenceInRuns()
    {
        await using var f=await SandboxFixture.Create();await CompleteMetadata(f);var fetcher=new Fetcher();var configuration=Service(f,fetcher);
        var session=await f.Service().CreateAsync(f.Actor,f.App.AppId,default);
        var executor=new MiniAppSandboxCheckExecutor(f.Db,f.Service(),configuration);
        var coordinator=new MiniAppSandboxRunCoordinator(f.Db,f.Service(),f.Clock,executor,new());
        var run=await coordinator.CreateAsync(f.Actor,f.App.AppId,new(session.Id,["domain","runtime","token"]),default);
        await coordinator.ExecuteNextAsync(default);var detail=await coordinator.GetAsync(f.Actor,f.App.AppId,run.Id,default);
        Assert.Equal("Completed",detail.Run.Status);Assert.Equal("Passed",Assert.Single(detail.Results,x=>x.Key=="domain").Status);
        Assert.Equal("NotImplemented",Assert.Single(detail.Results,x=>x.Key=="runtime").Status);
        Assert.Equal("Skipped",Assert.Single(detail.Results,x=>x.Key=="token").Status);
    }

    [Fact]
    public async Task RealCheckerCannotCommitPassedAfterConfigurationChangesDuringFetch()
    {
        await using var f=await SandboxFixture.Create();var fetcher=new Fetcher();var configuration=Service(f,fetcher);
        var session=await f.Service().CreateAsync(f.Actor,f.App.AppId,default);
        fetcher.DuringFetch=async()=>{f.App.Version=Guid.NewGuid();await f.Db.SaveChangesAsync();};
        var coordinator=new MiniAppSandboxRunCoordinator(f.Db,f.Service(),f.Clock,new MiniAppSandboxCheckExecutor(f.Db,f.Service(),configuration),new());
        var run=await coordinator.CreateAsync(f.Actor,f.App.AppId,new(session.Id,["website"]),default);await coordinator.ExecuteNextAsync(default);
        var detail=await coordinator.GetAsync(f.Actor,f.App.AppId,run.Id,default);Assert.Equal("Failed",detail.Run.Status);
        Assert.Equal("Skipped",Assert.Single(detail.Results).Status);Assert.Equal("MISSING_EVIDENCE",Assert.Single(detail.Results).MessageCode);
    }

    private sealed class ActualServerJson(HttpClient client):IPublicJsonFetcher
    {
        public async Task<PublicFetchResult> FetchJsonAsync(Uri uri,int maxBytes,CancellationToken ct)
        {
            using var response=await client.GetAsync(uri,ct);response.EnsureSuccessStatusCode();
            return new(await response.Content.ReadAsByteArrayAsync(ct),response.Content.Headers.ContentType!.MediaType!);
        }
    }
}
