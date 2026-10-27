using Microsoft.EntityFrameworkCore;
using Viora.Domain.Developers;
using Viora.Domain.Entities;
using Viora.Infrastructure.Developers;
using Xunit;

public sealed class MiniAppBridgeRevocationTests
{
    [Fact]
    public async Task RevocationRequiresTheCommandTransaction()
    {
        await using var f = await SandboxFixture.Create();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new MiniAppBridgeRevocation(f.Db, f.Clock).RevokeAllAsync(f.App.Id, default));
    }

    [Fact]
    public async Task ProductionRevocationIsIsolatedAndRollsBackWithThePublicationCommand()
    {
        await using var f = await SandboxFixture.Create();
        var account = new Account { Email = "bridge-revocation@example.test" };
        MiniAppBridgeSession Session(bool sandbox) => new()
        {
            MiniAppId = f.App.Id, AccountId = account.Id, ActorId = f.Actor.Id, SourceSessionId = f.Source.Id,
            ClientId = f.App.AppId, Origin = "https://partner.example.com", Policy = "test", IsSandbox = sandbox,
            CodeHash = Guid.NewGuid().ToString("N"), CodeChallenge = new string('a', 43),
            CodeExpiresAt = f.Clock.Now.UtcDateTime.AddSeconds(30), ExpiresAt = f.Clock.Now.UtcDateTime.AddMinutes(10)
        };
        var production = Session(false); var sandbox = Session(true);
        f.Db.AddRange(account, production, sandbox); await f.Db.SaveChangesAsync();
        var service = new MiniAppBridgeRevocation(f.Db, f.Clock);
        await using (var tx = await f.Db.Database.BeginTransactionAsync())
        {
            await service.RevokeProductionAsync(f.App.Id, default);
            Assert.NotNull(await f.Db.Set<MiniAppBridgeSession>().Where(x => x.Id == production.Id).Select(x => x.RevokedAt).SingleAsync());
            Assert.Null(await f.Db.Set<MiniAppBridgeSession>().Where(x => x.Id == sandbox.Id).Select(x => x.RevokedAt).SingleAsync());
            await tx.RollbackAsync();
        }
        Assert.All(await f.Db.Set<MiniAppBridgeSession>().AsNoTracking().ToArrayAsync(), x => Assert.Null(x.RevokedAt));
        await using (var tx = await f.Db.Database.BeginTransactionAsync())
        {
            await service.RevokeAllAsync(f.App.Id, default); await tx.CommitAsync();
        }
        Assert.All(await f.Db.Set<MiniAppBridgeSession>().AsNoTracking().ToArrayAsync(), x => Assert.NotNull(x.RevokedAt));
    }
}
