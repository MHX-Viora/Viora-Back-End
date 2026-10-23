using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Viora.Application.Developers;

namespace Viora.Infrastructure.Developers;

public sealed record MiniAppProductionBrowserBinding(Guid Id, string BrowserBindingHash, string BrowserNonceHash, DateTime ExpiresAt);
public sealed record MiniAppProductionPreparedBinding(MiniAppHostPrepareDto Public, string CookieCredential, DateTime CookieExpiresAt);

public sealed class MiniAppProductionBindingService(IDataProtectionProvider protection, TimeProvider clock, MiniAppProductionOptions options)
{
    private readonly IDataProtector browserProtector = protection.CreateProtector("MiniAppProduction.BrowserBinding.v1");
    private readonly IDataProtector challengeProtector = protection.CreateProtector("MiniAppProduction.LaunchBinding.v1");
    private sealed record Browser(Guid Id, string Secret, DateTime ExpiresAt, string Audience);
    private sealed record Challenge(Guid Id, string BrowserBindingHash, string BrowserNonceHash, DateTime ExpiresAt, string Audience);
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public MiniAppProductionPreparedBinding Prepare(string? cookie)
    {
        RequireEnabled();
        var browser = Read<Browser>(browserProtector, cookie);
        if (browser == null || !Valid(browser) || browser.ExpiresAt <= Now.AddSeconds(options.GrantSeconds))
        {
            browser = new(Guid.NewGuid(), MiniAppProductionCredentials.New(), Now.AddMinutes(30), options.HostOrigin);
            cookie = browserProtector.Protect(JsonSerializer.Serialize(browser));
        }
        var nonce = MiniAppProductionCredentials.New(); var expires = new[] { Now.AddMinutes(2), browser.ExpiresAt }.Min();
        var challenge = new Challenge(browser.Id, MiniAppProductionCredentials.Hash(browser.Secret), MiniAppProductionCredentials.Hash(nonce), expires, options.HostOrigin);
        return new(new(challengeProtector.Protect(JsonSerializer.Serialize(challenge)), nonce, expires), cookie!, browser.ExpiresAt);
    }

    public MiniAppProductionBrowserBinding RequireLaunch(string? ticket, string? nonce)
    {
        RequireEnabled(); var challenge = Read<Challenge>(challengeProtector, ticket);
        if (challenge == null || challenge.Id == Guid.Empty || challenge.Audience != options.HostOrigin || challenge.ExpiresAt <= Now ||
            challenge.ExpiresAt > Now.AddMinutes(2) || !MiniAppProductionCredentials.Valid(nonce) ||
            !MiniAppProductionCredentials.EqualHash(challenge.BrowserNonceHash, MiniAppProductionCredentials.Hash(nonce!))) throw Invalid();
        return new(challenge.Id, challenge.BrowserBindingHash, challenge.BrowserNonceHash, challenge.ExpiresAt);
    }

    public void RequireBrowser(MiniAppProductionBrowserBinding binding, string? cookie)
    {
        RequireEnabled(); var browser = Read<Browser>(browserProtector, cookie);
        if (browser == null || !Valid(browser) || binding.ExpiresAt <= Now || browser.Id != binding.Id ||
            !MiniAppProductionCredentials.EqualHash(binding.BrowserBindingHash, MiniAppProductionCredentials.Hash(browser.Secret))) throw Invalid();
    }
    private bool Valid(Browser value) => value.Id != Guid.Empty && value.Audience == options.HostOrigin && value.ExpiresAt > Now &&
        value.ExpiresAt <= Now.AddMinutes(30) && MiniAppProductionCredentials.Valid(value.Secret);
    private static T? Read<T>(IDataProtector protector, string? value) where T : class
    {
        if (value is not { Length: > 0 and <= 4096 }) return null;
        try { return JsonSerializer.Deserialize<T>(protector.Unprotect(value)); }
        catch (Exception e) when (e is CryptographicException or JsonException or ArgumentException) { return null; }
    }
    private void RequireEnabled() { if (!options.Enabled) throw new DeveloperAuthException(404, "HOST_UNAVAILABLE", "Mini App hiện không khả dụng."); }
    private static DeveloperAuthException Invalid() => new(403, "HOST_BINDING_INVALID", "Không thể xác minh trình duyệt. Vui lòng mở lại Mini App.");
}

public static class MiniAppProductionCredentials
{
    public static string New() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    public static bool Valid(string? value) => value is { Length: 43 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    public static bool EqualHash(string? a, string? b) => a is { Length: 64 } && b is { Length: 64 } &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));
}
