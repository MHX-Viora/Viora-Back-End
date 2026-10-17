using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class MiniAppSsoConfigurationTests
{
    [Fact]
    public async Task LegacyRotationInvalidatesTheVersionUsedByBackendProvisioning()
    {
        await using var host=await SsoProtocolTestHost.Create();await host.MakeConfidential("isolated-legacy-interleaving-secret");
        var owner=await host.WithServices(async s=>(await s.GetRequiredService<AppDbContext>().MiniApps.SingleAsync()).DeveloperAccountId);
        var before=Assert.Single((await host.WithServices(s=>Service(s).Get(owner,"app_protocol",default))).Clients);
        await host.WithServices(s=>ActivatorUtilities.CreateInstance<MiniAppService>(s).Credentials(owner,"app_protocol",true,default));
        await Assert.ThrowsAsync<DeveloperAuthException>(()=>host.WithServices(s=>Service(s).RotateCredential(owner,"app_protocol","Sandbox",before.Version,default)));
    }
    [Fact]
    public async Task DiagnosticsRejectStaleScopePermissions()
    {
        await using var host=await SsoProtocolTestHost.Create();
        await host.WithServices(async s=>
        {
            var app=await s.GetRequiredService<AppDbContext>().MiniApps.SingleAsync();var service=Service(s);
            Assert.True(Assert.Single((await service.Get(app.DeveloperAccountId,app.AppId,default)).Clients).Ready);
            var manager=s.GetRequiredService<IOpenIddictApplicationManager>();var projection=(await manager.FindByClientIdAsync(SsoProtocolTestHost.ClientId))!;
            var descriptor=new OpenIddictApplicationDescriptor();await manager.PopulateAsync(descriptor,projection);
            descriptor.Permissions.Remove(OpenIddictConstants.Permissions.Scopes.Profile);await manager.UpdateAsync(projection,descriptor);
            var client=Assert.Single((await service.Get(app.DeveloperAccountId,app.AppId,default)).Clients);
            Assert.False(client.Ready);Assert.Equal("SSO_PROJECTION_STALE",client.ErrorCode);return true;
        });
    }
    [Theory]
    [InlineData("https://PARTNER.example.com/callback")]
    [InlineData("https://partner.example.com:443/callback")]
    public async Task SavedRedirectsUseTheSameCanonicalFormAsTheLibrary(string redirect)
    {
        await using var host=await SsoProtocolTestHost.Create();
        await host.WithServices(async s=>
        {
            var app=await s.GetRequiredService<AppDbContext>().MiniApps.SingleAsync();var service=Service(s);
            var before=await service.Get(app.DeveloperAccountId,app.AppId,default);var client=Assert.Single(before.Clients);
            var after=await service.Save(app.DeveloperAccountId,app.AppId,"Sandbox",new("Public",true,true,["openid"],[redirect],before.AppVersion,client.Version),default);
            var saved=Assert.Single(after.Clients);Assert.True(saved.Ready);Assert.Equal(SsoProtocolTestHost.RedirectUri,Assert.Single(saved.RedirectUris));return true;
        });
    }
    private static MiniAppSsoService Service(IServiceProvider s) => ActivatorUtilities.CreateInstance<MiniAppSsoService>(s);
    [Fact]
    public async Task OwnerConfiguresPublicClientAtomicallyWithoutSecretAndOtherEnvironmentRemainsSeparate()
    {
        await using var host = await SsoProtocolTestHost.Create();
        await host.WithServices(async s =>
        {
            var db=s.GetRequiredService<AppDbContext>();var app=await db.MiniApps.SingleAsync();var service=Service(s);
            await Assert.ThrowsAsync<DeveloperAuthException>(()=>service.Get(Guid.NewGuid(),app.AppId,default));
            var initial=await service.Get(app.DeveloperAccountId,app.AppId,default);
            var sandbox=Assert.Single(initial.Clients);
            var saved=await service.Save(app.DeveloperAccountId,app.AppId,"Production",new("Public",true,false,["openid"],["https://partner.example.com/production-callback"],initial.AppVersion,null),default);
            Assert.Equal(2,saved.Clients.Count);
            var production=Assert.Single(saved.Clients,x=>x.Environment=="Production");
            Assert.Equal("Public",production.ClientType);Assert.False(production.Ready);Assert.Equal("SSO_CLIENT_UNAVAILABLE",production.ErrorCode);
            Assert.Equal(sandbox.ClientId,Assert.Single(saved.Clients,x=>x.Environment=="Sandbox").ClientId);
            var manager=s.GetRequiredService<IOpenIddictApplicationManager>();
            var projected=await manager.FindByClientIdAsync(production.ClientId);Assert.NotNull(projected);
            Assert.True(await manager.HasClientTypeAsync(projected,OpenIddictConstants.ClientTypes.Public));
            Assert.Equal(new[]{"https://partner.example.com/production-callback"},(await manager.GetRedirectUrisAsync(projected)).ToArray());
            Assert.Empty(await db.MiniAppClientSecrets.ToListAsync());
            var output=JsonSerializer.Serialize(saved);Assert.DoesNotContain("ClientSecret",output);Assert.DoesNotContain("SecretEncrypted",output);
            await Assert.ThrowsAsync<DeveloperAuthException>(()=>service.Save(app.DeveloperAccountId,app.AppId,"Sandbox",new("Public",true,true,["openid"],[SsoProtocolTestHost.RedirectUri],initial.AppVersion,sandbox.Version),default));
            return true;
        });
    }

    [Fact]
    public async Task ChangingConfidentialToPublicRevokesRegistrySecretAndLibraryAuthentication()
    {
        await using var host=await SsoProtocolTestHost.Create();const string secret="isolated-configuration-secret";await host.MakeConfidential(secret);
        await host.WithServices(async s=>
        {
            var db=s.GetRequiredService<AppDbContext>();var app=await db.MiniApps.SingleAsync();var service=Service(s);
            var initial=await service.Get(app.DeveloperAccountId,app.AppId,default);var client=Assert.Single(initial.Clients);
            await service.Save(app.DeveloperAccountId,app.AppId,"Sandbox",new("Public",true,true,["openid"],[SsoProtocolTestHost.RedirectUri],initial.AppVersion,client.Version),default);
            Assert.All(await db.MiniAppClientSecrets.ToListAsync(),x=>Assert.NotNull(x.RevokedAt));
            var manager=s.GetRequiredService<IOpenIddictApplicationManager>();var projection=(await manager.FindByClientIdAsync(client.ClientId))!;
            Assert.True(await manager.HasClientTypeAsync(projection,OpenIddictConstants.ClientTypes.Public));
            Assert.False(await manager.ValidateClientSecretAsync(projection,secret));
            return true;
        });
    }
}
