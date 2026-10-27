using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Domain.Entities;
using Viora.Infrastructure.Developers;
using Xunit;

public sealed class MiniAppReviewAccessTests
{
    [Fact]
    public async Task OwnerAccessIsAppBoundAndDoesNotGrantSandboxTesterAuthority()
    {
        await using var f = await SandboxFixture.Create();
        var access = new MiniAppReviewAccess(f.Db, f.Clock);
        Assert.Equal(f.App.Id, (await access.RequireOwnerAsync(f.Actor.Id, f.Source.Id, f.App.AppId, default)).Id);
        var denied = await Assert.ThrowsAsync<DeveloperAuthException>(() => access.RequireOwnerAsync(Guid.NewGuid(), f.Source.Id, f.App.AppId, default));
        Assert.Equal(404, denied.Status);
        await f.Db.MiniApps.Where(x => x.Id == f.App.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.DeletedAt, f.Clock.Now.UtcDateTime));
        denied = await Assert.ThrowsAsync<DeveloperAuthException>(() => access.RequireOwnerAsync(f.Actor.Id, f.Source.Id, f.App.AppId, default));
        Assert.Equal(404, denied.Status);
        Assert.Empty(f.Options.AdminTesterIds);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("absolute")]
    [InlineData("revoked")]
    [InlineData("suspended")]
    [InlineData("locked")]
    public async Task OwnerRequiresALiveActiveSourceSession(string invalid)
    {
        await using var f = await SandboxFixture.Create();
        var developer = await f.Db.DeveloperAccounts.SingleAsync();
        var now = f.Clock.Now.UtcDateTime;
        switch (invalid)
        {
            case "expired": f.Source.ExpiresAt = now; break;
            case "absolute": f.Source.AbsoluteExpiresAt = now; break;
            case "revoked": f.Source.RevokedAt = now; break;
            case "suspended": developer.Status = DeveloperStatus.Suspended; break;
            case "locked": developer.LockedUntil = now.AddMinutes(1); break;
        }
        await f.Db.SaveChangesAsync();
        var denied = await Assert.ThrowsAsync<DeveloperAuthException>(() => new MiniAppReviewAccess(f.Db, f.Clock)
            .RequireOwnerAsync(f.Actor.Id, f.Source.Id, f.App.AppId, default));
        Assert.Equal(403, denied.Status);
    }

    [Theory]
    [InlineData("moderator")]
    [InlineData("user")]
    [InlineData("revoked")]
    [InlineData("expired")]
    [InlineData("deleted")]
    [InlineData("banned")]
    [InlineData("other-session")]
    public async Task AdminIsRecheckedAgainstTheLiveAccountAndSignedSession(string invalid)
    {
        await using var f = await SandboxFixture.Create();
        var account = new Account { Email = "review-access@example.test", Role = AccountRole.Admin };
        var sid = Guid.NewGuid();
        var token = new RefreshToken { Account = account, SessionId = sid, TokenHash = "review-access-test", ExpiresAt = f.Clock.Now.UtcDateTime.AddHours(1) };
        f.Db.Add(token); await f.Db.SaveChangesAsync();
        var access = new MiniAppReviewAccess(f.Db, f.Clock);
        await access.RequireAdminAsync(account.Id, sid, default);
        switch (invalid)
        {
            case "moderator": account.Role = AccountRole.Moderator; break;
            case "user": account.Role = AccountRole.User; break;
            case "revoked": token.RevokedAt = f.Clock.Now.UtcDateTime; break;
            case "expired": token.ExpiresAt = f.Clock.Now.UtcDateTime; break;
            case "deleted": account.DeletedAt = f.Clock.Now.UtcDateTime; break;
            case "banned": account.Status = AccountStatus.Banned; break;
            case "other-session": sid = Guid.NewGuid(); break;
        }
        await f.Db.SaveChangesAsync();
        var denied = await Assert.ThrowsAsync<DeveloperAuthException>(() => access.RequireAdminAsync(account.Id, sid, default));
        Assert.Equal(403, denied.Status);
        Assert.Empty(f.Options.AdminTesterIds);
    }
}
