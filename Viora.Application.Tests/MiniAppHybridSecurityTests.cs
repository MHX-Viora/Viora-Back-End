using System.Net;
using Viora.Application.MiniApps;
using Xunit;

public sealed class MiniAppHybridSecurityTests
{
    [Theory]
    [InlineData("https://localhost/app")]
    [InlineData("https://127.0.0.1/app")]
    [InlineData("https://169.254.169.254/latest")]
    [InlineData("https://[::1]/")]
    [InlineData("https://example.com:8443/")]
    [InlineData("https://user@example.com/")]
    public void RegistryRejectsUnsafeUrls(string url) => Assert.False(MiniAppSecurityPolicy.IsAllowedHttpsUrl(url, [new Uri(url).Host]));

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.1")]
    [InlineData("192.168.1.1")]
    [InlineData("100.64.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("fc00::1")]
    [InlineData("fe80::1")]
    [InlineData("192.88.99.1")]
    [InlineData("2001:20::1")]
    [InlineData("3fff::1")]
    public void VerificationRejectsNonPublicAddresses(string ip) => Assert.False(MiniAppSecurityPolicy.IsPublicAddress(IPAddress.Parse(ip)));

    [Fact] public void ExactCallbackDoesNotAcceptSuffixes() => Assert.False(MiniAppSecurityPolicy.IsExactRedirect("https://example.com/callback/", ["https://example.com/callback"]));
    [Fact] public void PkceRequiresMatchingS256Verifier()
    {
        var verifier = new string('a', 43);
        var challenge = MiniAppSecurityPolicy.CreatePkceChallenge(verifier);
        Assert.True(MiniAppSecurityPolicy.ValidatePkce(verifier, challenge));
        Assert.False(MiniAppSecurityPolicy.ValidatePkce(new string('b', 43), challenge));
        Assert.False(MiniAppSecurityPolicy.ValidatePkce("short", challenge));
    }
}
