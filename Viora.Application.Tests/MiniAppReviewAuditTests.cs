using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Domain.Entities;
using Viora.Infrastructure.Developers;
using Xunit;

public sealed class MiniAppReviewAuditTests
{
    private static MiniAppReviewAuditEntry Entry(SandboxFixture f) => new()
    {
        MiniAppId = f.App.Id, Actor = new(MiniAppReviewActorType.Developer, f.Actor.Id), Action = MiniAppReviewActionKind.VersionUpdated,
        RequestId = Guid.NewGuid(), CorrelationId = Guid.NewGuid(), PayloadHash = MiniAppReviewAuditService.Hash(new { command = "fixture" }),
        BeforeState = "Draft", AfterState = "Draft", ResultJson = "{\"revision\":1}"
    };

    [Fact]
    public async Task AuditRequiresOwningTransactionAndRollsBackWithTheBusinessChange()
    {
        await using var f = await SandboxFixture.Create(); var service = new MiniAppReviewAuditService(f.Db); var entry = Entry(f);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AppendAsync(entry, default));
        await using (var tx = await f.Db.Database.BeginTransactionAsync())
        {
            await f.Db.MiniApps.ExecuteUpdateAsync(s => s.SetProperty(x => x.Name, "Rolled back"));
            await service.AppendAsync(entry, default);
            var saved = await service.FindAsync(entry.Actor, entry.MiniAppId, entry.RequestId, entry.PayloadHash, default);
            Assert.NotNull(saved); Assert.Equal(entry.ResultJson, saved.ResultJson);
            Assert.Equal(entry.Actor.Id, saved.ActorDeveloperId); Assert.Null(saved.ActorAdminAccountId);
            await Assert.ThrowsAsync<DeveloperAuthException>(() => service.FindAsync(entry.Actor, entry.MiniAppId, entry.RequestId, new string('0', 64), default));
            await tx.RollbackAsync();
        }
        Assert.Empty(await f.Db.MiniAppReviewActions.AsNoTracking().ToListAsync());
        Assert.Equal("Fixture", (await f.Db.MiniApps.AsNoTracking().SingleAsync()).Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AdminMirrorUsesOnlyAnActualSocialUserMapping(bool mapped)
    {
        await using var f = await SandboxFixture.Create();
        var admin = new Account { Email = "review-audit-admin@example.test", Role = AccountRole.Admin };
        var user = mapped ? new User { Account = admin, DisplayName = "Audit admin" } : null;
        f.Db.Add(admin); if (user != null) f.Db.Add(user); await f.Db.SaveChangesAsync();
        var entry = Entry(f) with { Actor = new(MiniAppReviewActorType.Admin, admin.Id), Action = MiniAppReviewActionKind.ReviewStarted };
        await using var tx = await f.Db.Database.BeginTransactionAsync();
        await new MiniAppReviewAuditService(f.Db).AppendAsync(entry, default); await tx.CommitAsync();
        var action = await f.Db.MiniAppReviewActions.AsNoTracking().SingleAsync(); Assert.Equal(admin.Id, action.ActorAdminAccountId); Assert.Null(action.ActorDeveloperId);
        if (mapped) { Assert.Equal(user!.Id, (await f.Db.AdminLogs.SingleAsync()).AdminId); Assert.NotEqual(admin.Id, user.Id); }
        else Assert.Empty(await f.Db.AdminLogs.ToListAsync());
    }

    [Theory]
    [InlineData("{\"clientSecret\":\"must-never-persist\"}")]
    [InlineData("{\"nested\":{\"access_token\":\"must-never-persist\"}}")]
    [InlineData("{\"x\":1,\"x\":2}")]
    [InlineData("not json")]
    public async Task UnsafeOrAmbiguousAuditPayloadIsRejected(string json)
    {
        await using var f = await SandboxFixture.Create(); await using var tx = await f.Db.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<DeveloperAuthException>(() => new MiniAppReviewAuditService(f.Db).AppendAsync(Entry(f) with { MetadataJson = json }, default));
        Assert.Empty(await f.Db.MiniAppReviewActions.ToListAsync());
    }
}
