using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Viora.Application.GroupCalls;
using Viora.Infrastructure.GroupCalls;
using Viora.Domain.Entities;
using Xunit;

namespace Viora.Infrastructure.Tests;

public sealed class LiveKitTokenIssuerTests
{
    [Fact]
    public void Issue_scopes_token_to_one_room_and_participant()
    {
        var options = Options.Create(new LiveKitOptions
        {
            Url = "wss://example.livekit.cloud",
            ApiKey = "test-key",
            ApiSecret = "test-secret-with-at-least-32-bytes"
        });
        var issuer = new LiveKitTokenIssuer(options);
        var callId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var credentials = issuer.Issue(callId, userId, "An", GroupCallType.Video);

        Assert.Equal(options.Value.Url, credentials.LiveKitUrl);
        var payload = DecodePayload(credentials.Token);
        Assert.Equal("test-key", payload.GetProperty("iss").GetString());
        Assert.Equal(userId.ToString(), payload.GetProperty("sub").GetString());
        Assert.Equal($"viora-group-{callId:N}", payload.GetProperty("video").GetProperty("room").GetString());
        Assert.True(payload.GetProperty("video").GetProperty("roomJoin").GetBoolean());
        Assert.True(payload.GetProperty("video").GetProperty("canPublish").GetBoolean());
        Assert.True(payload.GetProperty("video").GetProperty("canSubscribe").GetBoolean());
        Assert.InRange(
            payload.GetProperty("exp").GetInt64() - payload.GetProperty("nbf").GetInt64(),
            899,
            901);
    }

    [Fact]
    public void Issue_rejects_placeholder_configuration()
    {
        var issuer = new LiveKitTokenIssuer(Options.Create(new LiveKitOptions
        {
            Url = "wss://...",
            ApiKey = "...",
            ApiSecret = "..."
        }));

        Assert.Throws<LiveKitConfigurationException>(() =>
            issuer.Issue(Guid.NewGuid(), Guid.NewGuid(), "An", GroupCallType.Audio));
    }

    private static JsonElement DecodePayload(string token)
    {
        var segment = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        segment = segment.PadRight(segment.Length + (4 - segment.Length % 4) % 4, '=');
        return JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(segment))).RootElement.Clone();
    }
}
