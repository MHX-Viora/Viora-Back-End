using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Viora.Domain.Entities;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class SandboxAutoLoginProtocolTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReservedEmailReusesOnlyValidSandboxAccountAndNeverAdoptsRealUser(bool isSandbox)
    {
        await using var host = await SsoProtocolTestHost.Create(testIdentity: true);
        var owner = await SandboxTestIdentityTests.Developer(host);
        var existing = await host.WithServices(async s => {
            var db = s.GetRequiredService<AppDbContext>();
            var account = new Account { Email = "sandbox-test@domain.test", IsSandbox = isSandbox,
                User = new User { DisplayName = "ANKT Sandbox User", Gender = Gender.Male, IsVerified = true } };
            db.Accounts.Add(account); await db.SaveChangesAsync(); return account.Id;
        });
        if (!isSandbox)
        {
            await Assert.ThrowsAsync<Viora.Application.Developers.DeveloperAuthException>(() => SandboxTestIdentityTests.Launch(host, owner));
            await host.WithServices(async s => {
                Assert.Equal(0, await s.GetRequiredService<AppDbContext>().SandboxIdentityTemplates.CountAsync()); return true;
            });
            return;
        }
        await SandboxTestIdentityTests.Launch(host, owner);
        await host.WithServices(async s => {
            var db = s.GetRequiredService<AppDbContext>();
            Assert.Equal(existing, await db.SandboxIdentityTemplates.Select(x => x.AccountId).SingleAsync());
            Assert.Equal(existing, await db.SandboxTestIdentities.Select(x => x.AccountId).SingleAsync());
            Assert.Equal(1, await db.Accounts.IgnoreQueryFilters().CountAsync(x => x.IsSandbox)); return true;
        });
    }

    [Fact]
    public async Task RevokingOneSharedAccountSessionPreservesOtherTokenAndReadsCurrentDatabaseProfile()
    {
        await using var host = await SsoProtocolTestHost.Create(testIdentity: true);
        var owner = await SandboxTestIdentityTests.Developer(host);
        var first = await SandboxTestIdentityTests.Launch(host, owner);
        var second = await SandboxTestIdentityTests.Launch(host, owner);
        async Task<string> Token(Viora.Application.Developers.SandboxTestLaunch launch)
        {
            var cookie = await SandboxTestIdentityTests.TestCookie(host, launch);
            var code = await SandboxTestIdentityTests.TestCode(host, cookie);
            var response = await SsoTokenEndpointTests.Exchange(host, code);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("access_token").GetString()!;
        }
        var firstToken = await Token(first); var secondToken = await Token(second);
        await host.WithServices(async s => {
            await s.GetRequiredService<SandboxTestService>().RevokeAsync(owner.Owner, owner.Source, "app_protocol", first.SessionId, default);
            var db = s.GetRequiredService<AppDbContext>();
            var id = await db.SandboxIdentityTemplates.Select(x => x.AccountId).SingleAsync();
            await db.Users.IgnoreQueryFilters().Where(x => x.AccountId == id)
                .ExecuteUpdateAsync(x => x.SetProperty(u => u.Gender, Gender.Female).SetProperty(u => u.DisplayName, "Database profile"));
            return true;
        });
        using var revoked = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo"); revoked.Headers.Authorization = new("Bearer", firstToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.SendAsync(revoked)).StatusCode);
        using var valid = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo"); valid.Headers.Authorization = new("Bearer", secondToken);
        var info = await host.Client.SendAsync(valid); Assert.Equal(HttpStatusCode.OK, info.StatusCode);
        var profile = await info.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Database profile", profile.GetProperty("displayName").GetString());
        Assert.Equal("female", profile.GetProperty("gender").GetString());
        Assert.True(profile.GetProperty("isSandbox").GetBoolean());
    }
}
