using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.Sso;
using Xunit;

public sealed class SsoRegistryTests
{
    [Fact]
    public async Task RegistryCredentialRotationProjectionAndFailureRollbackUseOnePostgreSqlTransaction()
    {
        await using var pg = await IsolatedPostgres.Start();
        var services = new ServiceCollection(); services.AddLogging();
        services.AddDbContext<AppDbContext>(x => x.UseNpgsql(pg.ConnectionString));
        services.AddOpenIddict().AddCore(x => { x.DisableEntityCaching(); x.UseEntityFrameworkCore().UseDbContext<AppDbContext>(); });
        services.AddSingleton(Options.Create(new MiniAppOptions { ActiveKeyVersion = "test", EncryptionKeys = new() { ["test"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)) } }));
        services.AddSingleton<MiniAppSecretProtector>();
        services.AddSingleton<IHostEnvironment>(new RegistryEnvironment());
        services.AddScoped<MiniAppOidcRegistry>(); services.AddScoped<MiniAppService>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); await db.Database.EnsureCreatedAsync();
        var owner = new DeveloperAccount { FullName = "Test", EmailNormalized = "registry@example.test", Status = DeveloperStatus.Active, TermsAcceptedAt = DateTime.UtcNow };
        db.DeveloperAccounts.Add(owner); await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<MiniAppService>();
        var dto = await service.Create(owner.Id, new("Registry test", "utilities", Guid.NewGuid()), default);
        var app = await db.MiniApps.SingleAsync(); app.SsoEnabled = true;
        await db.SaveChangesAsync();
        var registry = scope.ServiceProvider.GetRequiredService<MiniAppOidcRegistry>();
        await using (var transaction = await db.Database.BeginTransactionAsync())
        { await registry.SynchronizeAsync(app.Id, default); await transaction.CommitAsync(); }
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var before = await service.Credentials(owner.Id, dto.AppId, false, default);
        var projected = (await manager.FindByClientIdAsync(before.ClientKey))!;
        Assert.True(await manager.ValidateClientSecretAsync(projected, before.ClientSecret));
        Assert.NotEqual(before.ClientSecret, (await db.Set<OpenIddictEntityFrameworkCoreApplication>().SingleAsync()).ClientSecret);
        var after = await service.Credentials(owner.Id, dto.AppId, true, default);
        projected = (await manager.FindByClientIdAsync(after.ClientKey))!;
        Assert.False(await manager.ValidateClientSecretAsync(projected, before.ClientSecret));
        Assert.True(await manager.ValidateClientSecretAsync(projected, after.ClientSecret));
        var publicClient = new MiniAppOAuthClient { MiniAppId = app.Id, ClientKey = "production-public", ClientType = "Public", Environment = MiniAppOAuthEnvironment.Production, AllowedScopes = "openid" };
        db.MiniAppOAuthClients.Add(publicClient);
        db.MiniAppRedirectUris.AddRange(
            new MiniAppRedirectUri { MiniAppId = app.Id, Environment = MiniAppOAuthEnvironment.Sandbox, RedirectUri = "https://sandbox.example.test/callback" },
            new MiniAppRedirectUri { MiniAppId = app.Id, Environment = MiniAppOAuthEnvironment.Production, RedirectUri = "https://production.example.test/callback" });
        await db.SaveChangesAsync();
        await using (var transaction = await db.Database.BeginTransactionAsync())
        { await registry.SynchronizeAsync(app.Id, default); await transaction.CommitAsync(); }
        var publicProjection = (await manager.FindByClientIdAsync(publicClient.ClientKey))!;
        Assert.Equal(OpenIddictConstants.ClientTypes.Public, await manager.GetClientTypeAsync(publicProjection));
        Assert.Null((await db.Set<OpenIddictEntityFrameworkCoreApplication>().SingleAsync(x => x.ClientId == publicClient.ClientKey)).ClientSecret);
        Assert.Equal("https://production.example.test/callback", Assert.Single(await manager.GetRedirectUrisAsync(publicProjection)));
        Assert.Equal("https://sandbox.example.test/callback", Assert.Single(await manager.GetRedirectUrisAsync(projected)));
        Assert.DoesNotContain(OpenIddictConstants.Permissions.Prefixes.Scope + "profile", await manager.GetPermissionsAsync(publicProjection));
        var authoritative = await db.MiniAppOAuthClients.SingleAsync(x => x.Environment == MiniAppOAuthEnvironment.Sandbox); authoritative.ClientType = "Invalid"; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<DeveloperAuthException>(() => service.Credentials(owner.Id, dto.AppId, true, default));
        db.ChangeTracker.Clear();
        var remaining = await db.MiniAppClientSecrets.SingleAsync(x => x.RevokedAt == null);
        Assert.Equal(after.ClientSecret, scope.ServiceProvider.GetRequiredService<MiniAppSecretProtector>().Decrypt(remaining.SecretEncrypted, remaining.SecretKeyVersion, remaining.OAuthClientId));
        authoritative = await db.MiniAppOAuthClients.SingleAsync(x => x.Environment == MiniAppOAuthEnvironment.Sandbox); authoritative.ClientType = "Confidential"; await db.SaveChangesAsync();
        await service.Delete(owner.Id, dto.AppId, default);
        Assert.Null(await manager.FindByClientIdAsync(after.ClientKey));
        Assert.Null(await manager.FindByClientIdAsync(publicClient.ClientKey));
    }

    private sealed class RegistryEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "SsoRegistryTests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
