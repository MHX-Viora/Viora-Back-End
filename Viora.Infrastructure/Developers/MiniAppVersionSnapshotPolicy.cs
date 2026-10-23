using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Viora.Application.Developers;
using Viora.Infrastructure.Sso;

namespace Viora.Infrastructure.Developers;

public static class MiniAppVersionSnapshotPolicy
{
    public const int MaxBytes = 65536;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16, NumberHandling = JsonNumberHandling.Strict
    };

    public static CanonicalMiniAppVersionSnapshot Create(MiniAppVersionSnapshot input)
    {
        if (input is null || input.SchemaVersion != 1 || !MiniAppInput.Categories.Contains(input.Category))
            throw MiniAppInput.Invalid("Cấu trúc snapshot không hợp lệ.");
        var website = Url(input.WebsiteUrl);
        var domain = website == null ? null : PublicWebAddress.Host(website);
        if (input.Domain != null && !string.Equals(input.Domain, domain, StringComparison.OrdinalIgnoreCase))
            throw MiniAppInput.Invalid("Domain snapshot không khớp website.");
        var snapshot = input with
        {
            Name = MiniAppInput.Text(input.Name, 120, true), LogoUrl = Url(input.LogoUrl),
            ShortDescription = Text(input.ShortDescription, 300), Description = Text(input.Description, 10000),
            WebsiteUrl = website, Domain = domain, PrivacyPolicyUrl = Url(input.PrivacyPolicyUrl),
            TermsOfServiceUrl = Url(input.TermsOfServiceUrl),
            SupportEmail = string.IsNullOrWhiteSpace(input.SupportEmail) ? null : DeveloperInput.Email(input.SupportEmail).ToLowerInvariant(),
            Sandbox = Intent(input.Sandbox, false), Production = Intent(input.Production, true)
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, Json);
        if (bytes.Length > MaxBytes) throw MiniAppInput.Invalid("Snapshot vượt giới hạn 64 KiB.");
        return new(snapshot, Encoding.UTF8.GetString(bytes), Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }

    public static CanonicalMiniAppVersionSnapshot Read(string json, string expectedHash)
    {
        try
        {
            if (json == null || expectedHash == null || Encoding.UTF8.GetByteCount(json) > MaxBytes ||
                expectedHash.Length != 64 || expectedHash.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
                throw Invalid();
            var bytes = Encoding.UTF8.GetBytes(json);
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), Convert.FromHexString(expectedHash))) throw Invalid();
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
            RejectDuplicates(document.RootElement);
            var snapshot = JsonSerializer.Deserialize<MiniAppVersionSnapshot>(bytes, Json) ?? throw Invalid();
            var canonical = Create(snapshot);
            if (!string.Equals(canonical.Json, json, StringComparison.Ordinal)) throw Invalid();
            return canonical;
        }
        catch (Exception error) when (error is JsonException or FormatException or DeveloperAuthException or ArgumentException)
        {
            throw Invalid();
        }
    }

    private static MiniAppVersionSsoIntent? Intent(MiniAppVersionSsoIntent? input, bool production)
    {
        if (input == null) return null;
        if (input.ClientType is not ("Public" or "Confidential") || input.ClientIdentity == Guid.Empty ||
            (input.ClientIdentity == null) != string.IsNullOrEmpty(input.ClientId) ||
            input.Scopes is not { Length: >= 1 and <= 3 } || !input.Scopes.Contains("openid", StringComparer.Ordinal) ||
            input.Scopes.Any(s => !SsoClientPolicy.SupportedScopes.Contains(s, StringComparer.Ordinal)) ||
            input.RedirectUris is not { Length: <= 20 } || production && input.SandboxEnabled)
            throw MiniAppInput.Invalid("Cấu hình SSO snapshot không hợp lệ.");
        var clientId = input.ClientId;
        if (clientId != null && (clientId.Length is < 1 or > 64 || clientId.Any(c => char.IsControl(c) || char.IsWhiteSpace(c))))
            throw MiniAppInput.Invalid("Client identity snapshot không hợp lệ.");
        return input with { ClientId = clientId, Scopes = input.Scopes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            RedirectUris = input.RedirectUris.Select(uri => new Uri(MiniAppInput.Url(uri, false, true)).AbsoluteUri)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray() };
    }

    private static string? Text(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : MiniAppInput.Text(value, max);
    private static string? Url(string? value) => string.IsNullOrWhiteSpace(value) ? null : PublicWebAddress.Normalize(value);
    private static DeveloperAuthException Invalid() => new(409, "VERSION_SNAPSHOT_INVALID", "Snapshot đã thay đổi hoặc không còn hợp lệ.");

    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!keys.Add(property.Name)) throw Invalid();
                RejectDuplicates(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicates(item);
    }
}
