using AgoraIO.Media;
using Microsoft.Extensions.Configuration;
using Viora.Infrastructure.LiveStreaming;
using Xunit;

namespace Viora.Application.Tests.Live;

public sealed class AgoraTokenServiceTests
{
    private const string AppId = "0123456789abcdef0123456789abcdef";
    private const string Certificate = "abcdef0123456789abcdef0123456789";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IssuedTokenHasOnlyTheExpectedPrivileges(bool broadcaster)
    {
        var service = new AgoraTokenService(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["AGORA_APP_ID"] = AppId, ["AGORA_APP_CERTIFICATE"] = Certificate }).Build());

        var response = service.Issue("live_test", 42, broadcaster);

        var parser = new AccessToken2();
        Assert.True(parser.parse(response.Token));
        Assert.True(parser.verifySignature(Certificate));
        var rtc = Assert.Single(parser.getServices(AccessToken2.SERVICE_TYPE_RTC));
        Assert.True(rtc.getPrivileges().ContainsKey((ushort)AccessToken2.PrivilegeRtcEnum.PRIVILEGE_JOIN_CHANNEL));
        Assert.Equal(broadcaster, rtc.getPrivileges().ContainsKey((ushort)AccessToken2.PrivilegeRtcEnum.PRIVILEGE_PUBLISH_VIDEO_STREAM));
        Assert.Equal(broadcaster, rtc.getPrivileges().ContainsKey((ushort)AccessToken2.PrivilegeRtcEnum.PRIVILEGE_PUBLISH_AUDIO_STREAM));
        Assert.Equal(broadcaster ? "broadcaster" : "audience", response.Role);
        Assert.Equal(42, response.Uid);
    }

    [Fact]
    public void MissingCredentialsCannotIssueToken()
    {
        var service = new AgoraTokenService(new ConfigurationBuilder().Build());
        Assert.False(service.IsConfigured);
        Assert.Throws<InvalidOperationException>(() => service.Issue("live_test", 42, true));
    }
}
