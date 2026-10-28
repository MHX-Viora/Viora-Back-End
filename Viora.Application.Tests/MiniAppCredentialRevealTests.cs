using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class MiniAppCredentialRevealTests
{
    private const string Client = "app_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Sandbox = "test-sandbox-secret-with-at-least-32-bytes";
    private const string Production = "test-production-secret-with-at-least-32-bytes";
    private static Task Configure(MiniAppAuthBridgeTests.Host h, bool mismatch = false, bool hashOnly = false) => h.Scope(async s => {
        var config=s.GetRequiredService<IConfiguration>();
        config["MiniAppAuth:Partners:Test:ClientId"]=Client;
        config["MiniAppAuth:Partners:Test:SandboxSecretHash"]=MiniAppBridgeService.Hash(Sandbox);
        config["MiniAppAuth:Partners:Test:ProductionSecretHash"]=MiniAppBridgeService.Hash(Production);
        config["MiniAppAuth:Partners:Test:SandboxSecret"]=hashOnly ? "" : mismatch ? Production : Sandbox;
        config["MiniAppAuth:Partners:Test:ProductionSecret"]=Production;
        s.GetRequiredService<IOptionsMonitorCache<MiniAppBridgeOptions>>().Clear();
        await s.GetRequiredService<MiniAppPartnerCredentialStore>().ImportLegacyAsync(s.GetRequiredService<IOptions<MiniAppBridgeOptions>>().Value, default);
        return true;
    });

    [Fact] public async Task OwnerCanRevealOnlyTheSelectedEnvironmentAndAuditIsRedacted()
    {
        await using var h=await MiniAppAuthBridgeTests.Host.Create();await Configure(h);
        foreach(var environment in new[]{"Sandbox","Production"}){
            var result=await h.Scope(s=>s.GetRequiredService<MiniAppPartnerLoginService>().RevealCredentialAsync(h.Owner,Client,environment,default));
            Assert.Equal(Client,result.ClientId);Assert.Equal(environment,result.Environment);
            Assert.Equal(environment=="Sandbox"?Sandbox:Production,result.ClientSecret);
            Assert.DoesNotContain(result.ClientSecret,result.ToString());
        }
        await h.Scope(async s=>{
            var audit=await s.GetRequiredService<AppDbContext>().MiniAppAuditLogs.Where(x=>x.Action=="partner.credential.revealed").ToArrayAsync();
            Assert.Equal(2,audit.Length);
            Assert.All(audit,x=>{Assert.DoesNotContain(Sandbox,x.Metadata);Assert.DoesNotContain(Production,x.Metadata);});
            return true;
        });
    }

    [Fact] public async Task AnotherDeveloperCannotRevealEvenWhenTheyKnowClientId()
    {
        await using var h=await MiniAppAuthBridgeTests.Host.Create();await Configure(h);
        var other=await h.Scope(async s=>{
            var db=s.GetRequiredService<AppDbContext>();
            var account=new DeveloperAccount{FullName="Other owner",EmailNormalized="OTHER@EXAMPLE.TEST",Status=DeveloperStatus.Active};
            var session=new DeveloperSession{Account=account,TokenHash="other-session",ExpiresAt=DateTime.UtcNow.AddHours(1),AbsoluteExpiresAt=DateTime.UtcNow.AddHours(1)};
            db.Add(session);await db.SaveChangesAsync();return new MiniAppBridgeActor(account.Id,session.Id,true);
        });
        var error=await Assert.ThrowsAsync<DeveloperAuthException>(()=>h.Scope(s=>s.GetRequiredService<MiniAppPartnerLoginService>().RevealCredentialAsync(other,Client,"Sandbox",default)));
        Assert.Equal(404,error.Status);Assert.DoesNotContain(Sandbox,error.Message);
    }

    [Fact] public async Task SocialAndRevokedDeveloperSessionsCannotReveal()
    {
        await using var h=await MiniAppAuthBridgeTests.Host.Create();await Configure(h);
        await Assert.ThrowsAsync<DeveloperAuthException>(()=>h.Scope(s=>s.GetRequiredService<MiniAppPartnerLoginService>().RevealCredentialAsync(h.User,Client,"Sandbox",default)));
        await h.Scope(async s=>{await s.GetRequiredService<AppDbContext>().DeveloperSessions.ExecuteUpdateAsync(x=>x.SetProperty(row=>row.RevokedAt,DateTime.UtcNow));return true;});
        var error=await Assert.ThrowsAsync<DeveloperAuthException>(()=>h.Scope(s=>s.GetRequiredService<MiniAppPartnerLoginService>().RevealCredentialAsync(h.Owner,Client,"Sandbox",default)));
        Assert.Equal(401,error.Status);
    }

    [Fact] public async Task MismatchedLegacySecretCannotBeImported()
    {
        await using var h=await MiniAppAuthBridgeTests.Host.Create();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Configure(h,mismatch:true));
    }

    [Fact] public async Task HashOnlyLegacySecretRemainsValidButRequiresRotationToReveal()
    {
        await using var h=await MiniAppAuthBridgeTests.Host.Create();await Configure(h,hashOnly:true);
        var value=await h.Scope(s=>s.GetRequiredService<MiniAppPartnerLoginService>().RevealCredentialAsync(h.Owner,Client,"Sandbox",default));
        Assert.Empty(value.ClientSecret);Assert.False(value.Revoked);Assert.NotEqual(Guid.Empty,value.Version);
        await h.Scope(async s=>{await s.GetRequiredService<MiniAppPartnerCredentialStore>().ValidateAsync(Client,Sandbox,true,default);return true;});
    }

    [Theory][InlineData("")][InlineData("production")][InlineData("Other")]
    public async Task EnvironmentMustBeExplicitAndExact(string environment)
    {
        await using var h=await MiniAppAuthBridgeTests.Host.Create();await Configure(h);
        var error=await Assert.ThrowsAsync<DeveloperAuthException>(()=>h.Scope(s=>s.GetRequiredService<MiniAppPartnerLoginService>().RevealCredentialAsync(h.Owner,Client,environment,default)));
        Assert.Equal(400,error.Status);
    }

    [Theory][InlineData("expired")][InlineData("locked")][InlineData("suspended")]
    public async Task ExpiredOrDisabledOwnersCannotReveal(string state)
    {
        await using var h=await MiniAppAuthBridgeTests.Host.Create();await Configure(h);
        await h.Scope(async s=>{
            var db=s.GetRequiredService<AppDbContext>();
            if(state=="expired")await db.DeveloperSessions.ExecuteUpdateAsync(x=>x.SetProperty(row=>row.ExpiresAt,DateTime.UtcNow.AddMinutes(-1)));
            else if(state=="locked")await db.DeveloperAccounts.ExecuteUpdateAsync(x=>x.SetProperty(row=>row.LockedUntil,DateTime.UtcNow.AddHours(1)));
            else await db.DeveloperAccounts.ExecuteUpdateAsync(x=>x.SetProperty(row=>row.Status,DeveloperStatus.Suspended));
            return true;
        });
        var error=await Assert.ThrowsAsync<DeveloperAuthException>(()=>h.Scope(s=>s.GetRequiredService<MiniAppPartnerLoginService>().RevealCredentialAsync(h.Owner,Client,"Sandbox",default)));
        Assert.Equal(401,error.Status);
    }
}
