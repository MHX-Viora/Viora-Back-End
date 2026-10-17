namespace Viora.Application.Sso;

public sealed record SsoAuthorizationDto(Guid Id, string AppId, string Name, string Environment, string[] Scopes,
    DateTime GrantedAt, DateTime? ExpiresAt, DateTime? RevokedAt);
