using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Xunit;

public sealed class MiniAppProductionDocumentTests
{
    [Fact]
    public async Task RunDocumentNeedsBothPrivateCookiesAndLivePolicyButCannotAuthorizeBridgeWithoutNonce()
    {
        await using var f = await ProductionRuntimeFixture.Create(); var service = f.Service();
        var document = await service.DocumentAsync(f.SessionId, f.Credential, f.Cookie, default);
        Assert.Equal(f.F.App.AppId, document.AppId); Assert.Equal("https://partner.example.com", document.PartnerOrigin);
        Assert.Equal(DateTimeKind.Utc, document.ExpiresAt.Kind);
        await Assert.ThrowsAsync<DeveloperAuthException>(() => service.DocumentAsync(f.SessionId, null, f.Cookie, default));
        await Assert.ThrowsAsync<DeveloperAuthException>(() => service.DocumentAsync(f.SessionId, f.Credential, null, default));
        await Assert.ThrowsAsync<DeveloperAuthException>(() => service.ContextAsync(f.SessionId, f.Credential, null, f.Cookie, default));
        await f.F.Db.MiniAppDomainVerifications.ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, f.F.Clock.Now.UtcDateTime));
        await Assert.ThrowsAsync<DeveloperAuthException>(() => service.DocumentAsync(f.SessionId, f.Credential, f.Cookie, default));
    }
}
