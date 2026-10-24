using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Options;

namespace viora_BE.Sso;

public sealed class SsoOptions
{
    public const string SectionName = "Sso";
    public bool Enabled { get; set; }
    public string Issuer { get; set; } = "";
    public string FirstPartyOrigin { get; set; } = "";
    public int AuthorizationCodeSeconds { get; set; } = 60;
    public int AccessTokenSeconds { get; set; } = 300;
    public string[] IntrospectionClientIds { get; set; } = [];
    public string SigningCertificatePath { get; set; } = "";
    public string? SigningCertificatePassword { get; set; }
    public string EncryptionCertificatePath { get; set; } = "";
    public string? EncryptionCertificatePassword { get; set; }
    public SsoCertificateFormat CertificateFormat { get; set; } = SsoCertificateFormat.Pfx;
    // Used only in Development, when neither explicit certificate path is set.
    public string DevelopmentCertificateDirectory { get; set; } = "";
    public SsoCertificateReference[] PreviousSigningCertificates { get; set; } = [];
    public SsoCertificateReference[] PreviousEncryptionCertificates { get; set; } = [];
}

public sealed class SsoCertificateReference
{
    public string Path { get; set; } = "";
    public string? Password { get; set; }
    public SsoCertificateFormat Format { get; set; } = SsoCertificateFormat.Pfx;
}

public enum SsoCertificateFormat { Pfx, Base64Pfx }

internal sealed class SsoOptionsValidator : IValidateOptions<SsoOptions>
{
    public ValidateOptionsResult Validate(string? name, SsoOptions options)
    {
        if (!options.Enabled) return ValidateOptionsResult.Success;
        var errors = new List<string>();
        if (!Uri.TryCreate(options.FirstPartyOrigin, UriKind.Absolute, out var origin) || origin.Scheme != Uri.UriSchemeHttps ||
            origin.AbsolutePath != "/" || origin.UserInfo.Length != 0 || origin.Query.Length != 0 || origin.Fragment.Length != 0)
            errors.Add("Sso:FirstPartyOrigin must be an absolute HTTPS origin without a path, credentials, query or fragment.");
        if (!Uri.TryCreate(options.Issuer, UriKind.Absolute, out var issuer) ||
            issuer.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(issuer.Host) ||
            issuer.UserInfo.Length != 0 || issuer.Query.Length != 0 || issuer.Fragment.Length != 0)
            errors.Add("Sso:Issuer must be an absolute HTTPS URI without credentials, query or fragment.");
        if (options.AuthorizationCodeSeconds is < 1 or > 60)
            errors.Add("Sso:AuthorizationCodeSeconds must be between 1 and 60.");
        if (options.AccessTokenSeconds is < 1 or > 300)
            errors.Add("Sso:AccessTokenSeconds must be between 1 and 300.");
        if (string.IsNullOrWhiteSpace(options.SigningCertificatePath) ||
            string.IsNullOrWhiteSpace(options.EncryptionCertificatePath))
            errors.Add(SsoCertificateFile.ConfigurationHelp);
        if (!Enum.IsDefined(options.CertificateFormat)) errors.Add("SSO certificate format must be Pfx or Base64Pfx.");
        if (options.PreviousSigningCertificates.Length > 3 || options.PreviousEncryptionCertificates.Length > 3)
            errors.Add("SSO permits at most three previous certificates per purpose.");
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}

// DI owns certificate lifetime. Never persist imported private keys to the machine store.
internal sealed class SsoCredentials : IDisposable
{
    public X509Certificate2 Signing { get; }
    public X509Certificate2 Encryption { get; }
    public IReadOnlyList<X509Certificate2> SigningCertificates => signingCertificates;
    public IReadOnlyList<X509Certificate2> EncryptionCertificates => encryptionCertificates;
    private readonly List<X509Certificate2> signingCertificates = [];
    private readonly List<X509Certificate2> encryptionCertificates = [];

    public SsoCredentials(IOptions<SsoOptions> configured)
    {
        var options = configured.Value;
        Signing = SsoCertificateFile.Load(options.SigningCertificatePath, options.SigningCertificatePassword, options.CertificateFormat, X509KeyUsageFlags.DigitalSignature);
        signingCertificates.Add(Signing);
        try
        {
            Encryption = SsoCertificateFile.Load(options.EncryptionCertificatePath, options.EncryptionCertificatePassword, options.CertificateFormat, X509KeyUsageFlags.KeyEncipherment);
            encryptionCertificates.Add(Encryption);
            foreach (var reference in options.PreviousSigningCertificates)
                signingCertificates.Add(SsoCertificateFile.Load(reference.Path, reference.Password, reference.Format, X509KeyUsageFlags.DigitalSignature));
            foreach (var reference in options.PreviousEncryptionCertificates)
                encryptionCertificates.Add(SsoCertificateFile.Load(reference.Path, reference.Password, reference.Format, X509KeyUsageFlags.KeyEncipherment));
            var publicKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var certificate in signingCertificates.Concat(encryptionCertificates))
            {
                using var key = certificate.GetRSAPublicKey()!;
                if (!publicKeys.Add(Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()))) throw InvalidCredentials();
            }
        }
        catch { Dispose(); throw; }
    }

    private static OptionsValidationException InvalidCredentials() => SsoCertificateFile.InvalidCredentials();

    public void Dispose()
    {
        foreach (var certificate in signingCertificates.Concat(encryptionCertificates)) certificate.Dispose();
    }
}

internal static class SsoCertificateFile
{
    internal const string ConfigurationHelp = "SSO certificates are unavailable or invalid. Run --provision-sso-certificates once outside the production container, install the protected files and passwords as secrets, and retain them across deploys. See docs/operations/sso-certificates-render.md. Production never generates replacement keys.";

    internal static X509Certificate2 Load(string path, string? password, SsoCertificateFormat format, X509KeyUsageFlags usage)
    {
        X509Certificate2? certificate = null;
        byte[]? bytes = null;
        try
        {
            if (format == SsoCertificateFormat.Pfx)
                certificate = new X509Certificate2(path, password, X509KeyStorageFlags.EphemeralKeySet);
            else if (format == SsoCertificateFormat.Base64Pfx)
            {
                // Render Secret Files contain text. Decode only in memory; never write to /etc/secrets.
                if (new FileInfo(path).Length > 128 * 1024) throw InvalidCredentials();
                bytes = Convert.FromBase64String(File.ReadAllText(path));
                certificate = new X509Certificate2(bytes, password, X509KeyStorageFlags.EphemeralKeySet);
            }
            else throw InvalidCredentials();
            using var key = certificate.GetRSAPrivateKey();
            var now = DateTime.UtcNow;
            if (!certificate.HasPrivateKey || key is null || key.KeySize < 2048 ||
                certificate.NotBefore.ToUniversalTime() > now || certificate.NotAfter.ToUniversalTime() <= now ||
                certificate.Extensions.OfType<X509KeyUsageExtension>().Any(x => (x.KeyUsages & usage) == 0))
                throw InvalidCredentials();
            return certificate;
        }
        catch (Exception error) when (error is CryptographicException or IOException or UnauthorizedAccessException or ArgumentException or FormatException or OptionsValidationException)
        {
            certificate?.Dispose();
            // Do not retain inner exceptions containing file paths or credential details.
            throw InvalidCredentials();
        }
        finally { if (bytes is not null) CryptographicOperations.ZeroMemory(bytes); }
    }

    internal static OptionsValidationException InvalidCredentials() => new(Options.DefaultName, typeof(SsoOptions),
        [ConfigurationHelp, "Use valid, distinct RSA certificates with private keys of at least 2048 bits and appropriate key usage; check passwords and certificate expiry."]);
}
