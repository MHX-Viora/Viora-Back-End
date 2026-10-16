using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using OpenIddict.Abstractions;

namespace viora_BE.Sso;

public sealed class SsoConsentInteraction(IDataProtectionProvider provider)
{
    private readonly ITimeLimitedDataProtector protector = provider.CreateProtector("ANKT.SSO.Consent.v1").ToTimeLimitedDataProtector();
    private static string Canonical(Guid accountId, OpenIddictRequest request) => accountId.ToString("D") + "\n" + JsonSerializer.Serialize(
        request.GetParameters().Where(x => x.Key is not ("consent_ticket" or "decision" or "__RequestVerificationToken"))
            .OrderBy(x => x.Key, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Value.ToString(), StringComparer.Ordinal));
    public string Issue(Guid accountId, OpenIddictRequest request) => protector.Protect(Canonical(accountId, request), TimeSpan.FromMinutes(5));
    public bool Validate(string? ticket, Guid accountId, OpenIddictRequest request)
    {
        if (ticket is not { Length: > 0 and <= 32768 }) return false;
        try { return string.Equals(protector.Unprotect(ticket, out _), Canonical(accountId, request), StringComparison.Ordinal); }
        catch (CryptographicException) { return false; }
    }
}
