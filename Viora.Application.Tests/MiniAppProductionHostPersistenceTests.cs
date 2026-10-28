using Microsoft.EntityFrameworkCore;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Xunit;

public sealed class MiniAppProductionHostPersistenceTests
{
    [Fact]
    public async Task AnonymousSessionRequiresAnExactPublicationAndBindingScopedRequestReceipt()
    {
        await using var f = await SandboxFixture.Create();
        await MiniAppPublicationPolicyTests.ApprovedPublication(f);
        var target = await MiniAppProductionEligibility.ReadAsync(f.Db, f.App.Id, f.Clock.Now.UtcDateTime, default);
        var session = Session(target); f.Db.Add(session); await f.Db.SaveChangesAsync();
        var duplicate = Session(target); duplicate.BindingId = session.BindingId; duplicate.RequestId = session.RequestId;
        f.Db.Add(duplicate); await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
        f.Db.ChangeTracker.Clear();
        var wrong = Session(target); wrong.MiniAppId = Guid.NewGuid(); f.Db.Add(wrong);
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("expiry")]
    [InlineData("platform")]
    [InlineData("hash")]
    [InlineData("runtime")]
    public async Task InvalidSessionStateCannotBePersisted(string field)
    {
        await using var f = await SandboxFixture.Create();
        await MiniAppPublicationPolicyTests.ApprovedPublication(f);
        var session = Session(await MiniAppProductionEligibility.ReadAsync(f.Db, f.App.Id, f.Clock.Now.UtcDateTime, default));
        if (field == "actor") session.SourceSocialSessionId = Guid.NewGuid();
        if (field == "expiry") session.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        if (field == "platform") session.Platform = "Unknown";
        if (field == "hash") session.BrowserBindingHash = "raw-browser-credential";
        if (field == "runtime") session.RuntimeCredentialHash = "unhashed";
        f.Db.Add(session); await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task GrantCannotBeMovedToAnotherBindingAndHashesAreUnique()
    {
        await using var f = await SandboxFixture.Create();
        await MiniAppPublicationPolicyTests.ApprovedPublication(f);
        var session = Session(await MiniAppProductionEligibility.ReadAsync(f.Db, f.App.Id, f.Clock.Now.UtcDateTime, default));
        f.Db.Add(session); await f.Db.SaveChangesAsync();
        var grant = Grant(session); grant.BindingId = Guid.NewGuid(); f.Db.Add(grant);
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
        f.Db.ChangeTracker.Clear();
        grant = Grant(session); f.Db.Add(grant); await f.Db.SaveChangesAsync();
        f.Db.Add(Grant(session)); await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
    }

    internal static MiniAppProductionHostSession Session(MiniAppProductionTarget target) => new()
    {
        MiniAppId = target.Publication.MiniAppId, PublicationId = target.Publication.Id,
        MiniAppVersionId = target.Version.Id, ApprovedReviewRequestId = target.Publication.ApprovedReviewRequestId,
        SnapshotHash = target.Version.SnapshotHash, PublicationRevision = target.Publication.ConcurrencyToken,
        OperationalRevision = Guid.NewGuid(), PolicyFingerprint = new('a', 64),
        BindingId = Guid.NewGuid(), BrowserBindingHash = new('b', 64), BrowserNonceHash = new('c', 64),
        RequestId = Guid.NewGuid(), RequestHash = new('d', 64), Platform = "Web",
        ExpiresAt = DateTime.UtcNow.AddMinutes(15), LastActivityAt = DateTime.UtcNow
    };
    internal static MiniAppProductionLaunchGrant Grant(MiniAppProductionHostSession session) => new()
    {
        HostSessionId = session.Id, BindingId = session.BindingId, GrantHash = new('e', 64),
        BrowserNonceHash = session.BrowserNonceHash, Audience = "https://host.example.com",
        ReceiptProtected = "fixture-protected-receipt", ExpiresAt = DateTime.UtcNow.AddSeconds(30)
    };
}
