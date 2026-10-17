namespace Viora.Application.Sso;

public sealed record SsoAccountLinkProof(string Challenge, DateTime ExpiresAt);
public sealed record SsoAccountLinkDto(Guid Id, string AppId, string Name, string Environment, string Subject,
    string PartnerAccountId, DateTime LinkedAt, DateTime? UnlinkedAt);
public sealed record SsoAccountLinkPreview(string AppId, string Name, string Environment, string PartnerAccountId, DateTime ExpiresAt);
