namespace Viora.Application.Developers;

public enum SandboxActorKind { Developer, Admin }
// Construct from authenticated server claims only, never a request body.
public sealed record SandboxActor(SandboxActorKind Kind, Guid Id, Guid SourceSessionId);
public sealed record SandboxSessionDto(Guid Id, string AppId, string Environment, DateTime CreatedAt, DateTime ExpiresAt,
    DateTime? RevokedAt, DateTime LastActivityAt, string ConfigurationFingerprint);
public sealed record SandboxLaunchRequest(string BrowserNonce);
public sealed record SandboxRedeemRequest(Guid SessionId,string Grant,string BrowserNonce);
public sealed record SandboxExternalLinkRequest(string Url);
