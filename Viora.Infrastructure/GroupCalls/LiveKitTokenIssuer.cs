using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Viora.Application.GroupCalls;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.GroupCalls;

public sealed class LiveKitOptions
{
    public string Url { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
}

public sealed class LiveKitTokenIssuer(IOptions<LiveKitOptions> options) : ILiveKitTokenIssuer
{
    private static readonly byte[] Header =
        JsonSerializer.SerializeToUtf8Bytes(new { alg = "HS256", typ = "JWT" });

    public LiveKitJoinCredentials Issue(
        Guid callId,
        Guid userId,
        string displayName,
        GroupCallType callType)
    {
        var configuration = options.Value;
        Validate(configuration);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var payload = new Dictionary<string, object?>
        {
            ["iss"] = configuration.ApiKey,
            ["sub"] = userId.ToString(),
            ["name"] = displayName,
            ["nbf"] = now,
            ["exp"] = now + 900,
            ["metadata"] = JsonSerializer.Serialize(new
            {
                callId,
                callType = (short)callType,
                userId
            }),
            ["video"] = new
            {
                roomJoin = true,
                room = $"viora-group-{callId:N}",
                canPublish = true,
                canSubscribe = true,
                canPublishData = true,
                canPublishSources = callType == GroupCallType.Video
                    ? new[] { "camera", "microphone" }
                    : new[] { "microphone" }
            }
        };
        var unsigned =
            $"{Base64Url(Header)}.{Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload))}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(configuration.ApiSecret));
        var signature = hmac.ComputeHash(Encoding.ASCII.GetBytes(unsigned));
        return new LiveKitJoinCredentials(
            configuration.Url,
            $"{unsigned}.{Base64Url(signature)}");
    }

    private static void Validate(LiveKitOptions value)
    {
        if (!Uri.TryCreate(value.Url, UriKind.Absolute, out var url) ||
            url.Scheme is not ("ws" or "wss") ||
            value.Url.Contains("...", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(value.ApiKey) ||
            value.ApiKey.Contains("...", StringComparison.Ordinal) ||
            Encoding.UTF8.GetByteCount(value.ApiSecret) < 32 ||
            value.ApiSecret.Contains("...", StringComparison.Ordinal))
        {
            throw new LiveKitConfigurationException(
                "LiveKit chưa được cấu hình trên máy chủ.");
        }
    }

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
}
