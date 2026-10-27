using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Entities;
using Viora.Infrastructure.Developers;
using Xunit;

public sealed class MiniAppProductionAccessTests
{
    [Fact]
    public async Task AnonymousNeedsNoSocialSessionAndAuthenticatedSourceMustStayLive()
    {
        await using var f = await SandboxFixture.Create();
        var access = new MiniAppProductionAccess(f.Db, f.Clock);
        await access.ValidateAsync(MiniAppProductionActor.Anonymous, default);
        var account = new Account { Email = "production-source@example.test" }; var sid = Guid.NewGuid();
        f.Db.AddRange(account, new RefreshToken { AccountId = account.Id, SessionId = sid,
            TokenHash = "production-source-fixture", ExpiresAt = f.Clock.Now.UtcDateTime.AddHours(1) });
        await f.Db.SaveChangesAsync();
        var actor = new MiniAppProductionActor(account.Id, sid);
        await access.ValidateAsync(actor, default);
        await Assert.ThrowsAsync<DeveloperAuthException>(() => access.ValidateAsync(new(account.Id, Guid.NewGuid()), default));
        await Assert.ThrowsAsync<DeveloperAuthException>(() => access.ValidateAsync(new(null, sid), default));
        account.Status = AccountStatus.Banned; await f.Db.SaveChangesAsync();
        Assert.Equal(401, (await Assert.ThrowsAsync<DeveloperAuthException>(() => access.ValidateAsync(actor, default))).Status);
        account.Status = AccountStatus.Active; await f.Db.SaveChangesAsync();
        await f.Db.RefreshTokens.ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, f.Clock.Now.UtcDateTime));
        await Assert.ThrowsAsync<DeveloperAuthException>(() => access.ValidateAsync(actor, default));

        Assert.Single(await f.Db.RefreshTokens.ToArrayAsync());
    }
}
