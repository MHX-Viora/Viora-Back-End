using Microsoft.AspNetCore.DataProtection;
using Viora.Application.Developers;
using Viora.Infrastructure.Developers;
using Xunit;

public sealed class MiniAppProductionBindingTests
{
    [Fact]
    public void BrowserCookieIsPrivateAndReusableWhileEachChallengeHasFreshNonce()
    {
        var clock = new SandboxClock(); var service = Service(clock);
        var first = service.Prepare(null); var next = service.Prepare(first.CookieCredential);
        Assert.NotEqual(first.Public.BrowserNonce, next.Public.BrowserNonce);
        Assert.NotEqual(first.Public.BindingId, next.Public.BindingId);
        var a = service.RequireLaunch(first.Public.BindingId, first.Public.BrowserNonce);
        var b = service.RequireLaunch(next.Public.BindingId, next.Public.BrowserNonce);
        Assert.Equal(a.Id, b.Id); Assert.Equal(a.BrowserBindingHash, b.BrowserBindingHash);
        service.RequireBrowser(a, first.CookieCredential);
        Assert.DoesNotContain(first.CookieCredential, first.Public.BindingId);
    }
    [Fact]
    public void WrongBrowserNonceAudienceOrExpiredChallengeCannotAuthorizeRedemption()
    {
        var clock = new SandboxClock(); var provider = new EphemeralDataProtectionProvider(); var service = Service(clock, provider);
        var first = service.Prepare(null); var other = service.Prepare(null);
        var binding = service.RequireLaunch(first.Public.BindingId, first.Public.BrowserNonce);
        Assert.Throws<DeveloperAuthException>(() => service.RequireBrowser(binding, other.CookieCredential));
        Assert.Throws<DeveloperAuthException>(() => service.RequireBrowser(binding, null));
        Assert.Throws<DeveloperAuthException>(() => service.RequireLaunch(first.Public.BindingId, other.Public.BrowserNonce));
        Assert.Throws<DeveloperAuthException>(() => service.RequireLaunch(first.Public.BindingId + "tampered", first.Public.BrowserNonce));
        Assert.Throws<DeveloperAuthException>(() => Service(clock, provider, "https://other.example.com").RequireLaunch(first.Public.BindingId, first.Public.BrowserNonce));
        clock.Now += TimeSpan.FromMinutes(3);
        Assert.Throws<DeveloperAuthException>(() => service.RequireLaunch(first.Public.BindingId, first.Public.BrowserNonce));
    }
    [Fact]
    public void DisabledModuleDoesNotIssueBindingAndMalformedCookieCannotSupplyBindingIdentity()
    {
        var clock = new SandboxClock(); var disabled = new MiniAppProductionBindingService(new EphemeralDataProtectionProvider(), clock, new());
        Assert.Equal(404, Assert.Throws<DeveloperAuthException>(() => disabled.Prepare(null)).Status);
        var service = Service(clock); var first = service.Prepare("forged cookie"); var second = service.Prepare("forged cookie");
        Assert.NotEqual(service.RequireLaunch(first.Public.BindingId, first.Public.BrowserNonce).Id,
            service.RequireLaunch(second.Public.BindingId, second.Public.BrowserNonce).Id);
    }
    internal static MiniAppProductionBindingService Service(SandboxClock clock, IDataProtectionProvider? provider = null, string host = "https://host.example.com") =>
        new(provider ?? new EphemeralDataProtectionProvider(), clock, new() { Enabled = true, HostOrigin = host });
}
