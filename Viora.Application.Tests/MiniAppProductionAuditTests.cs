using Microsoft.EntityFrameworkCore;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Xunit;

public sealed class MiniAppProductionAuditTests
{
    [Fact]
    public async Task AuditRequiresOwningTransactionAndRollsBackWithItsAction()
    {
        await using var f = await SandboxFixture.Create();
        await MiniAppPublicationPolicyTests.ApprovedPublication(f);
        var session = MiniAppProductionHostPersistenceTests.Session(await MiniAppProductionEligibility.ReadAsync(f.Db, f.App.Id, f.Clock.Now.UtcDateTime, default));
        f.Db.Add(session); await f.Db.SaveChangesAsync();
        var audit = new MiniAppProductionAudit(f.Db);
        Assert.Throws<InvalidOperationException>(() => audit.Append(session, MiniAppProductionEventCode.LaunchGranted));
        await using (var tx = await f.Db.Database.BeginTransactionAsync())
        {
            audit.Append(session, MiniAppProductionEventCode.LaunchGranted); await f.Db.SaveChangesAsync(); await tx.RollbackAsync();
        }
        f.Db.ChangeTracker.Clear(); Assert.Empty(await f.Db.Set<MiniAppProductionRuntimeEvent>().ToArrayAsync());
        await using (var tx = await f.Db.Database.BeginTransactionAsync())
        {
            audit.Append(session, MiniAppProductionEventCode.LaunchGranted); await f.Db.SaveChangesAsync(); await tx.CommitAsync();
        }
        var row = await f.Db.Set<MiniAppProductionRuntimeEvent>().SingleAsync();
        Assert.Equal(session.CorrelationId, row.CorrelationId); Assert.Null(row.AccountId);
    }
}
