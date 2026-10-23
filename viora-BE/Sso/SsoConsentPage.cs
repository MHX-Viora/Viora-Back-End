namespace viora_BE.Sso;

public sealed record SsoConsentPage(string MiniApp, string Developer, string? LogoUrl,
    IReadOnlyList<string> Scopes, IReadOnlyDictionary<string, string?> Parameters,
    string CsrfToken, string ConsentTicket, string StyleNonce, string Environment);
