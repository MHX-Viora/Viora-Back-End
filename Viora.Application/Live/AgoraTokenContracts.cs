namespace Viora.Application.Live;

public sealed record AgoraTokenResponse(string AppId, string ChannelName, string Token, long Uid, string Role, DateTimeOffset ExpiresAt);

public interface IAgoraTokenService
{
    bool IsConfigured { get; }
    string AppId { get; }
    AgoraTokenResponse Issue(string channelName, long uid, bool broadcaster);
}
