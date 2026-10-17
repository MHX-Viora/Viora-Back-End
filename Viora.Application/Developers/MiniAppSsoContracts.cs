namespace Viora.Application.Developers;

public sealed record MiniAppSsoRequest(string ClientType, bool SsoEnabled, bool SandboxEnabled, string[] Scopes,
    string[] RedirectUris, Guid AppVersion, Guid? ClientVersion);
public sealed record MiniAppSsoClientDto(string ClientId, string ClientType, string Environment, bool SandboxEnabled,
    string[] Scopes, string[] RedirectUris, Guid Version, bool Ready, string? ErrorCode);
public sealed record MiniAppSsoServerDto(bool Enabled, string? Issuer, string? Discovery, string? AuthorizationEndpoint,
    string? TokenEndpoint, string? UserInfoEndpoint, string? JwksUri, bool RefreshEnabled);
public sealed record MiniAppSsoDto(string AppId, bool SsoEnabled, Guid AppVersion, IReadOnlyList<MiniAppSsoClientDto> Clients,
    MiniAppSsoServerDto? Server = null);
public sealed record MiniAppSsoCredentialRotationRequest(Guid ClientVersion);
public sealed record MiniAppSsoCredentialDto(string ClientId,string ClientSecret,Guid ClientVersion);
