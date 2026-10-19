using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Infrastructure.Developers;
using Xunit;

public sealed class MiniAppSandboxGrantTests
{
    [Fact]
    public async Task GrantNeedsThePreparedBrowserAndIsConsumedOnceWithoutStoringCredentials()
    {
        await using var f=await SandboxFixture.Create(); var sessions=f.Service();
        var grants=new MiniAppSandboxGrantService(f.Db,sessions,f.Clock,f.Options,new EphemeralDataProtectionProvider());
        var session=await sessions.CreateAsync(f.Actor,f.App.AppId,default); var browser=grants.Prepare();
        var launch=await grants.IssueAsync(f.Actor,f.App.AppId,session.Id,browser.Nonce,default);
        Assert.DoesNotContain(launch.Grant,(await f.Db.MiniAppSandboxLaunchGrants.SingleAsync()).GrantHash);
        await Assert.ThrowsAsync<DeveloperAuthException>(()=>grants.RedeemAsync(session.Id,launch.Grant,browser.Nonce,"forged-binding",default));
        Assert.Null((await f.Db.MiniAppSandboxLaunchGrants.AsNoTracking().SingleAsync()).ConsumedAt);
        var runtime=await grants.RedeemAsync(session.Id,launch.Grant,browser.Nonce,browser.Binding,default);
        Assert.Equal(session.Id,(await grants.RequireRuntimeAsync(runtime.Credential,default)).Id);
        Assert.DoesNotContain(runtime.Credential,(await f.Db.MiniAppSandboxSessions.AsNoTracking().SingleAsync()).RuntimeCredentialHash!);
        await Assert.ThrowsAsync<DeveloperAuthException>(()=>grants.RedeemAsync(session.Id,launch.Grant,browser.Nonce,browser.Binding,default));
        await sessions.RevokeAsync(f.Actor,f.App.AppId,session.Id,default);
        await Assert.ThrowsAsync<DeveloperAuthException>(()=>grants.RequireRuntimeAsync(runtime.Credential,default));
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("nonce")]
    [InlineData("session")]
    [InlineData("revoked")]
    public async Task InvalidBoundGrantNeverCreatesRuntimeAuthority(string change)
    {
        await using var f=await SandboxFixture.Create(); var sessions=f.Service();
        var grants=new MiniAppSandboxGrantService(f.Db,sessions,f.Clock,f.Options,new EphemeralDataProtectionProvider());
        var session=await sessions.CreateAsync(f.Actor,f.App.AppId,default); var browser=grants.Prepare();
        var launch=await grants.IssueAsync(f.Actor,f.App.AppId,session.Id,browser.Nonce,default);
        if(change=="expired") f.Clock.Now+=TimeSpan.FromSeconds(31);
        if(change=="revoked") await sessions.RevokeAsync(f.Actor,f.App.AppId,session.Id,default);
        await Assert.ThrowsAsync<DeveloperAuthException>(()=>grants.RedeemAsync(change=="session"?Guid.NewGuid():session.Id,launch.Grant,
            change=="nonce"?new string('a',43):browser.Nonce,browser.Binding,default));
        Assert.Null((await f.Db.MiniAppSandboxSessions.AsNoTracking().SingleAsync()).RuntimeCredentialHash);
    }
}
