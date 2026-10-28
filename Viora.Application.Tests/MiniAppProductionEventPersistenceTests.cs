using Microsoft.EntityFrameworkCore;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Xunit;

public sealed class MiniAppProductionEventPersistenceTests
{
    [Fact]
    public async Task AuditUsesTypedFieldsAndRequiresTheSameAppSession()
    {
        await using var f = await SandboxFixture.Create();
        await MiniAppPublicationPolicyTests.ApprovedPublication(f);
        var session = MiniAppProductionHostPersistenceTests.Session(await MiniAppProductionEligibility.ReadAsync(f.Db, f.App.Id, f.Clock.Now.UtcDateTime, default));
        f.Db.Add(session); await f.Db.SaveChangesAsync();
        var audit = Event(session); f.Db.Add(audit); await f.Db.SaveChangesAsync();
        audit.Code = MiniAppProductionEventCode.Closed;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Db.SaveChangesAsync());
        f.Db.ChangeTracker.Clear();
        var wrong = Event(session); wrong.MiniAppId = Guid.NewGuid(); f.Db.Add(wrong);
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
        Assert.Equal(new[] { "Id", "CreatedAt", "MiniAppId", "HostSessionId", "AccountId", "Code", "CorrelationId" }.Order(),
            typeof(MiniAppProductionRuntimeEvent).GetProperties().Select(x => x.Name).Order());
    }

    [Fact]
    public async Task UnknownEventCodeCannotBePersisted()
    {
        await using var f = await SandboxFixture.Create();
        var row = new MiniAppProductionRuntimeEvent { MiniAppId = f.App.Id, Code = (MiniAppProductionEventCode)100, CorrelationId = Guid.NewGuid() };
        f.Db.Add(row); await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
    }
    internal static MiniAppProductionRuntimeEvent Event(MiniAppProductionHostSession session) => new()
    { MiniAppId = session.MiniAppId, HostSessionId = session.Id, AccountId = session.AccountId, Code = MiniAppProductionEventCode.LaunchGranted, CorrelationId = session.CorrelationId };
}
