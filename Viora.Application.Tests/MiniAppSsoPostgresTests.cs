using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;
using Viora.Application.Developers;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class MiniAppSsoPostgresTests
{
    [Fact]
    public async Task PostgreSqlExpiredConfidentialCredentialCanBeReplacedWithoutDuplicateActiveSecrets()
    {
        await using var pg=await IsolatedPostgres.Start();await using var host=await SsoProtocolTestHost.Create(pg);
        await host.MakeConfidential("isolated-expired-configuration-secret");
        await host.WithServices(async s=>
        {
            var db=s.GetRequiredService<AppDbContext>();var app=await db.MiniApps.SingleAsync();
            await db.MiniAppClientSecrets.ExecuteUpdateAsync(x=>x.SetProperty(p=>p.ExpiresAt,DateTime.UtcNow.AddMinutes(-1)));
            var initial=await Service(s).Get(app.DeveloperAccountId,app.AppId,default);var client=Assert.Single(initial.Clients);
            var saved=await Service(s).Save(app.DeveloperAccountId,app.AppId,"Sandbox",new("Confidential",true,true,["openid"],client.RedirectUris,initial.AppVersion,client.Version),default);
            Assert.True(Assert.Single(saved.Clients).Ready);
            Assert.Equal(1,await db.MiniAppClientSecrets.CountAsync(x=>x.RevokedAt==null));
            var credential=await Service(s).Credential(app.DeveloperAccountId,app.AppId,"Sandbox",default);
            Assert.NotEqual("isolated-expired-configuration-secret",credential);
            var manager=s.GetRequiredService<IOpenIddictApplicationManager>();var projection=(await manager.FindByClientIdAsync(client.ClientId))!;
            Assert.True(await manager.ValidateClientSecretAsync(projection,credential));return true;
        });
    }
    [Fact]
    public async Task PostgreSqlCredentialRotationRejectsOldSecretAndStaleWriter()
    {
        await using var pg=await IsolatedPostgres.Start();await using var host=await SsoProtocolTestHost.Create(pg);
        const string oldSecret="isolated-rotation-old-secret";await host.MakeConfidential(oldSecret);
        var owner=await host.WithServices(async s=>(await s.GetRequiredService<AppDbContext>().MiniApps.SingleAsync()).DeveloperAccountId);
        var client=Assert.Single((await host.WithServices(s=>Service(s).Get(owner,"app_protocol",default))).Clients);
        var rotated=await host.WithServices(s=>Service(s).RotateCredential(owner,"app_protocol","Sandbox",client.Version,default));
        await Assert.ThrowsAsync<DeveloperAuthException>(()=>host.WithServices(s=>Service(s).RotateCredential(owner,"app_protocol","Sandbox",client.Version,default)));
        await host.WithServices(async s=>
        {
            var manager=s.GetRequiredService<IOpenIddictApplicationManager>();var projection=(await manager.FindByClientIdAsync(client.ClientId))!;
            Assert.False(await manager.ValidateClientSecretAsync(projection,oldSecret));Assert.True(await manager.ValidateClientSecretAsync(projection,rotated.ClientSecret));
            var db=s.GetRequiredService<AppDbContext>();Assert.Equal(1,await db.MiniAppClientSecrets.CountAsync(x=>x.RevokedAt==null));
            var logs=System.Text.Json.JsonSerializer.Serialize(await db.MiniAppAuditLogs.ToListAsync());Assert.DoesNotContain(oldSecret,logs);Assert.DoesNotContain(rotated.ClientSecret,logs);
            return true;
        });
    }
    private static MiniAppSsoService Service(IServiceProvider s)=>ActivatorUtilities.CreateInstance<MiniAppSsoService>(s);
    [Fact]
    public async Task PostgreSqlConfigurationProjectionFailureRollsBackRegistryAndCallbacks()
    {
        await using var pg=await IsolatedPostgres.Start();var failure=new RejectProjection();
        await using var host=await SsoProtocolTestHost.Create(pg,options=>options.AddInterceptors(failure));
        var owner=await host.WithServices(async s=>(await s.GetRequiredService<AppDbContext>().MiniApps.SingleAsync()).DeveloperAccountId);
        var before=await host.WithServices(s=>Service(s).Get(owner,"app_protocol",default));var original=Assert.Single(before.Clients);
        failure.Armed=true;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>host.WithServices(s=>Service(s).Save(owner,"app_protocol","Sandbox",
            new("Confidential",true,true,["openid"],["https://partner.example.com/changed"],before.AppVersion,original.Version),default)));
        failure.Armed=false;
        await host.WithServices(async s=>
        {
            var after=await Service(s).Get(owner,"app_protocol",default);var client=Assert.Single(after.Clients);
            Assert.Equal(before.AppVersion,after.AppVersion);Assert.Equal(original.Version,client.Version);
            Assert.Equal("Public",client.ClientType);Assert.Equal(original.RedirectUris,client.RedirectUris);Assert.True(client.Ready);
            Assert.Empty(await s.GetRequiredService<AppDbContext>().MiniAppClientSecrets.ToListAsync());return true;
        });
    }
    [Fact]
    public async Task PostgreSqlConcurrentConfigurationsHaveOneWinnerAndMatchingLibraryProjection()
    {
        await using var pg=await IsolatedPostgres.Start();await using var host=await SsoProtocolTestHost.Create(pg);
        var owner=await host.WithServices(async s=>(await s.GetRequiredService<AppDbContext>().MiniApps.SingleAsync()).DeveloperAccountId);
        var initial=await host.WithServices(s=>Service(s).Get(owner,"app_protocol",default));var client=Assert.Single(initial.Clients);
        async Task<bool> Save(string callback)
        {
            try{await host.WithServices(s=>Service(s).Save(owner,"app_protocol","Sandbox",new("Public",true,true,["openid"],[callback],initial.AppVersion,client.Version),default));return true;}
            catch(DeveloperAuthException e) when(e.Status==409){return false;}
            catch(DbUpdateException){return false;}
            catch(Npgsql.PostgresException e) when(e.SqlState is "40001" or "40P01"){return false;}
        }
        Assert.Single(await Task.WhenAll(Save("https://partner.example.com/a"),Save("https://partner.example.com/b")),x=>x);
        await host.WithServices(async s=>
        {
            var result=Assert.Single((await Service(s).Get(owner,"app_protocol",default)).Clients);Assert.True(result.Ready);
            var manager=s.GetRequiredService<IOpenIddictApplicationManager>();var projection=(await manager.FindByClientIdAsync(result.ClientId))!;
            Assert.Equal(result.RedirectUris,(await manager.GetRedirectUrisAsync(projection)).ToArray());return true;
        });
    }
    private sealed class RejectProjection:SaveChangesInterceptor
    {
        public bool Armed;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken cancellationToken=default)
        {
            if(Armed && data.Context!.ChangeTracker.Entries<OpenIddictEntityFrameworkCoreApplication>().Any(x=>x.State is EntityState.Modified or EntityState.Added))
                throw new InvalidOperationException("Isolated test projection failure.");
            return ValueTask.FromResult(result);
        }
    }
}
