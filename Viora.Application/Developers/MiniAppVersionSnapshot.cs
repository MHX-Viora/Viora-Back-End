namespace Viora.Application.Developers;

// Only reviewed configuration belongs here. Credentials and protocol payloads stay in their existing stores.
public sealed record MiniAppVersionSnapshot(int SchemaVersion, string Name, string Category, string? LogoUrl,
    string? ShortDescription, string? Description, string? WebsiteUrl, string? Domain, string? PrivacyPolicyUrl,
    string? TermsOfServiceUrl, string? SupportEmail, bool SsoEnabled,
    MiniAppVersionSsoIntent? Sandbox, MiniAppVersionSsoIntent? Production);

public sealed record MiniAppVersionSsoIntent(Guid? ClientIdentity, string? ClientId, string ClientType,
    string[] Scopes, string[] RedirectUris, bool SandboxEnabled);

public sealed record CanonicalMiniAppVersionSnapshot(MiniAppVersionSnapshot Snapshot, string Json, string Hash);
