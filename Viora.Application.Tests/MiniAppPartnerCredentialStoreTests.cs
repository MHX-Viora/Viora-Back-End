using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class MiniAppPartnerCredentialStoreTests
{
    private const string Client = "app_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task OwnerProvisionsStableDistinctEnvironmentSecretsWithoutPartnerEnv()
    {
        await using var h = await MiniAppAuthBridgeTests.Host.Create();
        var production = await h.Scope(s => s.GetRequiredService<MiniAppPartnerLoginService>().RevealCredentialAsync(h.Owner, Client, "Production", default));
        var sandbox = await h.Scope(s => s.GetRequiredService<MiniAppPartnerLoginService>().RevealCredentialAsync(h.Owner, Client, "Sandbox", default));
        Assert.NotEqual(production.ClientSecret, sandbox.ClientSecret);
        Assert.Equal(43, production.ClientSecret.Length);
        var repeat = await h.Scope(s => s.GetRequiredService<MiniAppPartnerLoginService>().RevealCredentialAsync(h.Owner, Client, "Production", default));
        Assert.Equal(production.ClientSecret, repeat.ClientSecret);
        await h.Scope(async s => {
            var db = s.GetRequiredService<AppDbContext>();
            var rows = await db.Set<MiniAppPartnerCredential>().ToArrayAsync();
            Assert.Equal(2, rows.Length);
            Assert.All(rows, row => {
                Assert.DoesNotContain(production.ClientSecret, row.SecretEncrypted);
                Assert.DoesNotContain(sandbox.ClientSecret, row.SecretEncrypted);
                Assert.Equal(64, row.SecretHash.Length);
            });
            return true;
        });
    }

    [Fact]
    public async Task RotationAndRevocationRejectOldSecretsAndStaleUpdates()
    {
        await using var h = await MiniAppAuthBridgeTests.Host.Create();
        var first = await h.Scope(s => s.GetRequiredService<MiniAppPartnerLoginService>().RevealCredentialAsync(h.Owner, Client, "Sandbox", default));
        var rotated = await h.Scope(s => s.GetRequiredService<MiniAppPartnerLoginService>().ChangeCredentialAsync(h.Owner, Client, "Sandbox", first.Version, false, default));
        Assert.NotEqual(first.ClientSecret, rotated.ClientSecret);
        await Assert.ThrowsAsync<DeveloperAuthException>(() => h.Scope(async s => { await s.GetRequiredService<MiniAppPartnerCredentialStore>().ValidateAsync(Client, first.ClientSecret, true, default); return true; }));
        await h.Scope(async s => { await s.GetRequiredService<MiniAppPartnerCredentialStore>().ValidateAsync(Client, rotated.ClientSecret, true, default); return true; });
        var stale = await Assert.ThrowsAsync<DeveloperAuthException>(() => h.Scope(s => s.GetRequiredService<MiniAppPartnerLoginService>().ChangeCredentialAsync(h.Owner, Client, "Sandbox", first.Version, true, default)));
        Assert.Equal(409, stale.Status);
        var revoked = await h.Scope(s => s.GetRequiredService<MiniAppPartnerLoginService>().ChangeCredentialAsync(h.Owner, Client, "Sandbox", rotated.Version, true, default));
        Assert.True(revoked.Revoked); Assert.Empty(revoked.ClientSecret);
        var repeat = await h.Scope(s => s.GetRequiredService<MiniAppPartnerLoginService>().RevealCredentialAsync(h.Owner, Client, "Sandbox", default));
        Assert.True(repeat.Revoked); Assert.Empty(repeat.ClientSecret);
        await Assert.ThrowsAsync<DeveloperAuthException>(() => h.Scope(async s => { await s.GetRequiredService<MiniAppPartnerCredentialStore>().ValidateAsync(Client, rotated.ClientSecret, true, default); return true; }));
        var restored = await h.Scope(s => s.GetRequiredService<MiniAppPartnerLoginService>().ChangeCredentialAsync(h.Owner, Client, "Sandbox", revoked.Version, false, default));
        Assert.False(restored.Revoked); Assert.NotEqual(rotated.ClientSecret, restored.ClientSecret);
    }

    [Fact]
    public async Task EnvImportIsIdempotentAndCannotResurrectARevokedCredential()
    {
        await using var h = await MiniAppAuthBridgeTests.Host.Create();
        var legacy = new MiniAppBridgeOptions();
        const string secret = "import-fixture-secret-with-at-least-32-bytes";
        legacy.Partners["Fixture"] = new() { ClientId = Client, ProductionSecret = secret, ProductionSecretHash = MiniAppBridgeService.Hash(secret) };
        Assert.Equal(1, await h.Scope(s => s.GetRequiredService<MiniAppPartnerCredentialStore>().ImportLegacyAsync(legacy, default)));
        Assert.Equal(0, await h.Scope(s => s.GetRequiredService<MiniAppPartnerCredentialStore>().ImportLegacyAsync(legacy, default)));
        var value = await h.Scope(s => s.GetRequiredService<MiniAppPartnerLoginService>().RevealCredentialAsync(h.Owner, Client, "Production", default));
        Assert.Equal(secret, value.ClientSecret);
        await h.Scope(s => s.GetRequiredService<MiniAppPartnerLoginService>().ChangeCredentialAsync(h.Owner, Client, "Production", value.Version, true, default));
        Assert.Equal(0, await h.Scope(s => s.GetRequiredService<MiniAppPartnerCredentialStore>().ImportLegacyAsync(legacy, default)));
        var revoked = await h.Scope(s => s.GetRequiredService<MiniAppPartnerLoginService>().RevealCredentialAsync(h.Owner, Client, "Production", default));
        Assert.True(revoked.Revoked);
    }

    [Fact]
    public async Task ProvisionedSecretsCannotAuthenticateOtherAppsOrEnvironments()
    {
        await using var h = await MiniAppAuthBridgeTests.Host.Create();
        var first = await h.Scope(s => s.GetRequiredService<MiniAppPartnerLoginService>().RevealCredentialAsync(h.Owner, Client, "Sandbox", default));
        var otherClient = "app_" + new string('b', 43);
        await h.Scope(async s => {
            var db = s.GetRequiredService<AppDbContext>();
            db.MiniApps.Add(new() { AppId = otherClient, Name = "Second", Category = "other", DeveloperAccountId = h.Owner.Id, CreateRequestId = Guid.NewGuid() });
            await db.SaveChangesAsync(); return true;
        });
        var other = await h.Scope(s => s.GetRequiredService<MiniAppPartnerLoginService>().RevealCredentialAsync(h.Owner, otherClient, "Sandbox", default));
        Assert.NotEqual(first.ClientSecret, other.ClientSecret);
        foreach (var request in new[] { (otherClient, true), (Client, false) }) {
            await Assert.ThrowsAsync<DeveloperAuthException>(() => h.Scope(async s => {
                await s.GetRequiredService<MiniAppPartnerCredentialStore>().ValidateAsync(request.Item1, first.ClientSecret, request.Item2, default); return true;
            }));
        }
        var forbidden = await Assert.ThrowsAsync<DeveloperAuthException>(() => h.Scope(s => s.GetRequiredService<MiniAppPartnerLoginService>()
            .ChangeCredentialAsync(h.User, Client, "Sandbox", first.Version, true, default)));
        Assert.Equal(401, forbidden.Status);
    }

    [Fact]
    public async Task InvalidLegacyEntryRollsBackTheWholeImport()
    {
        await using var h = await MiniAppAuthBridgeTests.Host.Create();
        var legacy = new MiniAppBridgeOptions();
        const string secret = "import-fixture-secret-with-at-least-32-bytes";
        legacy.Partners["Fixture"] = new() { ClientId = Client, SandboxSecret = secret, SandboxSecretHash = MiniAppBridgeService.Hash(secret),
            ProductionSecret = secret, ProductionSecretHash = new string('0', 64) };
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Scope(s => s.GetRequiredService<MiniAppPartnerCredentialStore>().ImportLegacyAsync(legacy, default)));
        Assert.Equal(0, await h.Scope(s => s.GetRequiredService<AppDbContext>().Set<MiniAppPartnerCredential>().CountAsync()));
    }

    [Fact]
    public async Task PostgreSqlConcurrentProvisionAndRotationHaveOneWinner()
    {
        await using var postgres = await IsolatedPostgres.Start();
        await using var h = await MiniAppAuthBridgeTests.Host.Create(postgres);
        Task<MiniAppCredentialValue> Reveal() => h.Scope(s => s.GetRequiredService<MiniAppPartnerLoginService>().RevealCredentialAsync(h.Owner, Client, "Sandbox", default));
        var reveals = await Task.WhenAll(Reveal(), Reveal());
        Assert.Equal(reveals[0].ClientSecret, reveals[1].ClientSecret);
        Assert.Equal(reveals[0].Version, reveals[1].Version);
        async Task<MiniAppCredentialValue?> Rotate() {
            try { return await h.Scope(s => s.GetRequiredService<MiniAppPartnerLoginService>().ChangeCredentialAsync(h.Owner, Client, "Sandbox", reveals[0].Version, false, default)); }
            catch (DeveloperAuthException error) when (error.Status == 409) { return null; }
        }
        var results = await Task.WhenAll(Rotate(), Rotate());
        var winner = Assert.Single(results, value => value is not null)!;
        Assert.Equal(winner.ClientSecret, (await Reveal()).ClientSecret);
    }
}
