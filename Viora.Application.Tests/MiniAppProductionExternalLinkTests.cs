using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Infrastructure.Developers;
using Xunit;

public sealed class MiniAppProductionExternalLinkTests
{
    [Fact]
    public async Task ExternalTargetIsFrozenAndProbedOutsideTransactionThenRevalidated()
    {
        await using var f = await ProductionRuntimeFixture.Create();
        await f.F.Db.MiniApps.ExecuteUpdateAsync(s => s.SetProperty(x => x.WebsiteUrl, "https://draft.example.com/forged"));
        var fetcher = new MiniAppReviewReadinessTests.Fetcher { DuringFetch = () => { Assert.Null(f.F.Db.Database.CurrentTransaction); return Task.CompletedTask; } };
        var result = await new MiniAppProductionExternalLinkService(f.Service(), fetcher, f.F.Clock)
            .PrepareAsync(f.SessionId, f.Credential, f.Nonce, f.Cookie, default);
        Assert.Equal("https://partner.example.com/app", result.Url); Assert.Equal(1, fetcher.Calls);
        Assert.True(result.ExpiresAt > f.F.Clock.Now.UtcDateTime); Assert.True(result.ExpiresAt <= f.F.Clock.Now.UtcDateTime.AddSeconds(15));
    }
    [Fact]
    public async Task NetworkFailureAndRevocationDuringProbeNeverReturnAnExternalUrl()
    {
        await using var f = await ProductionRuntimeFixture.Create();
        var blocked = new MiniAppReviewReadinessTests.Fetcher { DuringFetch = () => throw new PublicFetchException("DOMAIN_BLOCKED", "private provider details") };
        var service = new MiniAppProductionExternalLinkService(f.Service(), blocked, f.F.Clock);
        var error = await Assert.ThrowsAsync<DeveloperAuthException>(() => service.PrepareAsync(f.SessionId, f.Credential, f.Nonce, f.Cookie, default));
        Assert.DoesNotContain("private provider", error.Message);
        var changed = new MiniAppReviewReadinessTests.Fetcher { DuringFetch = async () =>
            await f.F.Db.MiniAppDomainVerifications.ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, f.F.Clock.Now.UtcDateTime)) };
        await Assert.ThrowsAsync<DeveloperAuthException>(() => new MiniAppProductionExternalLinkService(f.Service(), changed, f.F.Clock)
            .PrepareAsync(f.SessionId, f.Credential, f.Nonce, f.Cookie, default));
    }
}
