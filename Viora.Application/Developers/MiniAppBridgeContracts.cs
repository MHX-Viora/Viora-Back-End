using System.Text.Json.Serialization;

namespace Viora.Application.Developers;

public sealed record MiniAppBridgeActor(Guid Id, Guid SourceSessionId, bool IsSandbox);
public sealed record MiniAppBridgeLaunchRequest(string ClientId, string CodeChallenge, string Origin, string Scope = "profile");
public sealed record MiniAppBridgeConfig(string ClientId, string LaunchUrl, string Origin, string Environment, string[] Scopes);
public sealed record MiniAppBridgeLaunch(Guid SessionId, string Code, int ExpiresIn, string ClientId, string Origin, string Environment);
public sealed record MiniAppBridgeExchange(
    [property: JsonPropertyName("client_id")] string ClientId,
    [property: JsonPropertyName("session_id")] Guid SessionId, string Code,
    [property: JsonPropertyName("code_verifier")] string CodeVerifier);
public sealed record MiniAppBridgeToken(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("expires_in")] int ExpiresIn, string Scope,
    [property: JsonPropertyName("session_id")] Guid SessionId, string Environment);
public sealed record MiniAppBridgeUser(Guid Id, string Username, string DisplayName, string? AvatarUrl,
    string? CoverUrl, string Gender, bool IsSandbox, string? FullName);

public sealed record MiniAppLoginCode(string Code, int ExpiresIn);
public sealed record MiniAppPartnerExchange(string Code);
public sealed record MiniAppPartnerSessionRequest(Guid SessionId);
public sealed record MiniAppPartnerIdentity(MiniAppPartnerUser User, string MiniAppId, string Environment, Guid SessionId, DateTime ExpiresAt);
public sealed record MiniAppPartnerUser(Guid Id, string DisplayName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AvatarUrl,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CoverUrl,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Gender);
