using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace viora_BE.Developer;

/// <summary>Authenticates only rate-limit client attribution. It never grants account access.</summary>
public static class DeveloperIngress
{
    public static string ClientIp(HttpContext context, string secret)
    {
        return TryClientIp(context, secret, out var ip) ? ip : context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
    public static bool TryClientIp(HttpContext context, string secret, out string ip)
    {
        ip = context.Request.Headers["X-ANKT-Bff-IP"].ToString();
        if (secret.Length < 32) return false;
        var time = context.Request.Headers["X-ANKT-Bff-Time"].ToString();
        var signature = context.Request.Headers["X-ANKT-Bff-Signature"].ToString();
        if (!IPAddress.TryParse(ip, out _) || !long.TryParse(time, out var timestamp) || signature.Length != 64 ||
            timestamp < DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 60 || timestamp > DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60) return false;
        byte[] supplied;
        try { supplied = Convert.FromHexString(signature); } catch (FormatException) { return false; }
        var message = $"{ip}\n{time}\n{context.Request.Method}\n{context.Request.Path}";
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(message));
        return CryptographicOperations.FixedTimeEquals(expected, supplied);
    }
}
