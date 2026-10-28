using System.Net;
using Viora.Application.Developers;
using Viora.Infrastructure.Developers;
using Xunit;

public sealed class PublicWebAddressTests
{
    [Theory]
    [InlineData("http://partner.example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://localhost")]
    [InlineData("https://foo.localhost")]
    [InlineData("https://metadata.google.internal")]
    [InlineData("https://127.0.0.1")]
    [InlineData("https://2130706433")]
    [InlineData("https://10.1.1.1")]
    [InlineData("https://[::1]")]
    [InlineData("https://[fd00::1]")]
    [InlineData("https://example.com/#fragment")]
    [InlineData("https://user:password@example.com")]
    [InlineData("https://example.com:8443")]
    [InlineData("https://example.com\\@localhost")]
    public void UnsafeStoredWebsiteRejected(string value) => Assert.Throws<DeveloperAuthException>(() => PublicWebAddress.Normalize(value));

    [Fact]
    public void WebsiteNormalizedWithoutLosingPathOrQuery() => Assert.Equal("https://example.com/path?a=1", PublicWebAddress.Normalize(" https://EXAMPLE.com:443/path?a=1 "));

    [Theory]
    [InlineData("0.0.0.0")][InlineData("10.1.2.3")][InlineData("100.64.0.1")][InlineData("127.0.0.1")]
    [InlineData("169.254.169.254")][InlineData("172.16.0.1")][InlineData("192.168.1.1")][InlineData("192.0.2.1")]
    [InlineData("198.18.0.1")][InlineData("198.51.100.1")][InlineData("203.0.113.1")][InlineData("224.0.0.1")][InlineData("255.255.255.255")]
    [InlineData("::")][InlineData("::1")][InlineData("::ffff:8.8.8.8")][InlineData("fc00::1")][InlineData("fe80::1")]
    [InlineData("ff02::1")][InlineData("64:ff9b::a00:1")][InlineData("2002:7f00:1::")][InlineData("2001::1")]
    [InlineData("2001:db8::1")][InlineData("3fff::1")]
    public void NonPublicAddressesRejected(string address) => Assert.False(PublicNetwork.IsPublic(IPAddress.Parse(address)));

    [Theory]
    [InlineData("8.8.8.8")][InlineData("1.1.1.1")][InlineData("2606:4700:4700::1111")]
    public void GlobalAddressesAccepted(string address) => Assert.True(PublicNetwork.IsPublic(IPAddress.Parse(address)));
}
