using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Domain.Entities;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.Security;
using Xunit;

public sealed class SandboxTestIdentityTests
{
    [Fact]
    public async Task BridgeIdentityPersistsLinkedProfileAcrossServiceScopes()
    {
        await using var host = await MiniAppAuthBridgeTests.Host.Create();
        async Task<SandboxIdentityTemplate> Identity() => await host.Scope(s => s.GetRequiredService<SandboxTestService>().BridgeIdentityAsync(default));
        var first = await Identity(); var second = await Identity();
        Assert.Equal(first.AccountId, second.AccountId);
        Assert.Equal("ankt_sandbox_test", second.Nickname);
        Assert.Equal("ANKT Developer Test", second.FullName);
        Assert.Equal("ANKT Sandbox User", second.Account.User!.DisplayName);
        Assert.True(SandboxTestOptions.CloudinaryUrl(second.Account.User.AvatarUrl));
        Assert.True(SandboxTestOptions.CloudinaryUrl(second.Account.User.CoverUrl));
        Assert.True(second.Account.IsSandbox);
        Assert.Equal(second.AccountId, second.Account.User.AccountId);
        Assert.Null(second.Account.PasswordHash);
        var jwt = new JwtTokenService(Options.Create(new JwtOptions { Key = new string('k', 64) }));
        Assert.Throws<InvalidOperationException>(() => jwt.CreateTokens(second.Account));
        await host.Scope(async s => {
            var db = s.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.Accounts.IgnoreQueryFilters().CountAsync(x => x.IsSandbox));
            Assert.Equal(1, await db.Users.CountAsync());
            Assert.False(await db.Users.AnyAsync(x => x.Id == second.Account.User.Id));
            Assert.Equal(1, await db.SandboxIdentityTemplates.CountAsync()); return true;
        });
    }

    [Fact]
    public async Task ReservedEmailCannotConvertExistingRealAccountIntoSandbox()
    {
        await using var host = await MiniAppAuthBridgeTests.Host.Create();
        await host.Scope(async s => {
            var db = s.GetRequiredService<AppDbContext>();
            db.Accounts.Add(new Account { Email = "sandbox-test@domain.test", User = new User { DisplayName = "Real account" } });
            await db.SaveChangesAsync(); return true;
        });
        var error = await Assert.ThrowsAsync<DeveloperAuthException>(() => host.Scope(s =>
            s.GetRequiredService<SandboxTestService>().BridgeIdentityAsync(default)));
        Assert.Equal("SANDBOX_TEMPLATE_CONFLICT", error.Code);
    }

    [Fact]
    public async Task DisabledSandboxCannotSeedIdentity()
    {
        await using var host = await MiniAppAuthBridgeTests.Host.Create();
        await host.Scope(async s => {
            var db = s.GetRequiredService<AppDbContext>();
            var service = new SandboxTestService(db, Options.Create(new SandboxTestOptions { Enabled = false }), TimeProvider.System);
            await Assert.ThrowsAsync<DeveloperAuthException>(() => service.BridgeIdentityAsync(default));
            Assert.False(await db.SandboxIdentityTemplates.AnyAsync()); return true;
        });
    }
}
