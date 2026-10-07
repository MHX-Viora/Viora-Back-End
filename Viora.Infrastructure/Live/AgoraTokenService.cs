using AgoraIO.Media;
using Microsoft.Extensions.Configuration;
using Viora.Application.Live;

namespace Viora.Infrastructure.LiveStreaming;

public sealed class AgoraTokenService(IConfiguration configuration) : IAgoraTokenService
{
    private readonly string _appId = configuration["AGORA_APP_ID"] ?? string.Empty;
    private readonly string _certificate = configuration["AGORA_APP_CERTIFICATE"] ?? string.Empty;

    public bool IsConfigured => _appId.Length == 32 && _certificate.Length == 32 &&
        _appId.All(Uri.IsHexDigit) && _certificate.All(Uri.IsHexDigit);
    public string AppId => _appId;

    public AgoraTokenResponse Issue(string channelName, long uid, bool broadcaster)
    {
        if (!IsConfigured) throw new InvalidOperationException("Agora credentials are not configured.");
        if (uid is < 1 or > uint.MaxValue) throw new ArgumentOutOfRangeException(nameof(uid));
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);
        var token = RtcTokenBuilder2.buildTokenWithUid(_appId, _certificate, channelName, (uint)uid,
            broadcaster ? RtcTokenBuilder2.Role.RolePublisher : RtcTokenBuilder2.Role.RoleSubscriber,
            3600, 3600);
        return new AgoraTokenResponse(_appId, channelName, token, uid, broadcaster ? "broadcaster" : "audience", expiresAt);
    }
}
