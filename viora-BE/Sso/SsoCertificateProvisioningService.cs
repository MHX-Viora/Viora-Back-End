using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using Microsoft.Extensions.Options;

namespace viora_BE.Sso;

public sealed record SsoCertificateBundle(string SigningPath, string EncryptionPath,
    string SigningPassword, string EncryptionPassword)
{
    public override string ToString() => "SSO certificate bundle (credentials redacted)";
}

/// <summary>
/// Local, atomic one-time provisioning. Not a distributed secret store: production
/// startup only loads externally installed secrets and never calls Provision.
/// </summary>
public sealed class SsoCertificateProvisioningService(IHostEnvironment? environment = null)
{
    public void Prepare(SsoOptions options)
    {
        if (!options.Enabled) return;
        var hasSigning = !string.IsNullOrWhiteSpace(options.SigningCertificatePath);
        var hasEncryption = !string.IsNullOrWhiteSpace(options.EncryptionCertificatePath);
        // Explicit configuration always wins. Partial configuration must fail validation.
        if (hasSigning || hasEncryption) return;
        if (environment?.IsDevelopment() == true)
        {
            var directory = string.IsNullOrWhiteSpace(options.DevelopmentCertificateDirectory)
                ? Path.Combine(environment.ContentRootPath, ".sso-certificates", "current")
                : options.DevelopmentCertificateDirectory;
            var bundle = Provision(directory);
            options.SigningCertificatePath = bundle.SigningPath;
            options.EncryptionCertificatePath = bundle.EncryptionPath;
            options.SigningCertificatePassword = bundle.SigningPassword;
            options.EncryptionCertificatePassword = bundle.EncryptionPassword;
            options.CertificateFormat = SsoCertificateFormat.Pfx;
            return;
        }
        // Conventional, externally managed Render Secret Files. Never write here.
        options.SigningCertificatePath = "/etc/secrets/ankt-sso-signing.pfx.b64";
        options.EncryptionCertificatePath = "/etc/secrets/ankt-sso-encryption.pfx.b64";
        options.CertificateFormat = SsoCertificateFormat.Base64Pfx;
    }

    public static SsoCertificateBundle Provision(string directory)
    {
        string? staging = null;
        try
        {
            var destination = Path.GetFullPath(directory);
            if (Directory.Exists(destination)) return ReadBundle(destination);
            if (File.Exists(destination)) throw SsoCertificateFile.InvalidCredentials();
            var parent = Path.GetDirectoryName(destination)!;
            PrivateDirectory(parent);
            staging = Path.Combine(parent, ".provision-" + Guid.NewGuid().ToString("N"));
            PrivateDirectory(staging);
            var signingPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var encryptionPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            WriteCertificate(staging, "signing", signingPassword, X509KeyUsageFlags.DigitalSignature);
            WriteCertificate(staging, "encryption", encryptionPassword, X509KeyUsageFlags.KeyEncipherment);
            WritePrivate(staging, "signing.password", System.Text.Encoding.UTF8.GetBytes(signingPassword));
            WritePrivate(staging, "encryption.password", System.Text.Encoding.UTF8.GetBytes(encryptionPassword));
            WritePrivate(staging, "render.env", System.Text.Encoding.UTF8.GetBytes(RenderEnvironment(signingPassword, encryptionPassword)));
            // Verify the complete protected pair before publishing it.
            _ = ReadBundle(staging);
            try { Directory.Move(staging, destination); }
            catch (IOException) when (Directory.Exists(destination))
            {
                // A concurrent local process won the atomic publish. Never overwrite it.
            }
            return ReadBundle(destination);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException or FormatException or OptionsValidationException)
        {
            // Do not retain exceptions containing paths, secret data or provider details.
            throw SsoCertificateFile.InvalidCredentials();
        }
        finally
        {
            // Only delete the private staging directory created by this invocation.
            if (staging is not null && Directory.Exists(staging))
            {
                try { Directory.Delete(staging, true); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                { throw SsoCertificateFile.InvalidCredentials(); }
            }
        }
    }

    private static void WriteCertificate(string directory, string purpose, string password, X509KeyUsageFlags usage)
    {
        using var rsa = RSA.Create(3072);
        var request = new CertificateRequest("CN=ANKT SSO " + purpose, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(usage, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        var now = DateTimeOffset.UtcNow;
        using var certificate = request.CreateSelfSigned(now.AddMinutes(-5), now.AddYears(2));
        var pfx = certificate.Export(X509ContentType.Pfx, password);
        try
        {
            WritePrivate(directory, "ankt-sso-" + purpose + ".pfx", pfx.ToArray());
            WritePrivate(directory, "ankt-sso-" + purpose + ".pfx.b64", System.Text.Encoding.ASCII.GetBytes(Convert.ToBase64String(pfx)));
        }
        finally { CryptographicOperations.ZeroMemory(pfx); }
    }

    private static SsoCertificateBundle ReadBundle(string directory)
    {
        RejectLink(directory);
        var files = new[] { "ankt-sso-signing.pfx", "ankt-sso-encryption.pfx", "signing.password", "encryption.password",
            "ankt-sso-signing.pfx.b64", "ankt-sso-encryption.pfx.b64", "render.env" };
        foreach (var file in files) RejectLink(Path.Combine(directory, file));
        var bundle = new SsoCertificateBundle(Path.Combine(directory, files[0]), Path.Combine(directory, files[1]),
            File.ReadAllText(Path.Combine(directory, files[2])), File.ReadAllText(Path.Combine(directory, files[3])));
        if (bundle.SigningPassword.Length < 32 || bundle.EncryptionPassword.Length < 32) throw SsoCertificateFile.InvalidCredentials();
        using var signing = SsoCertificateFile.Load(bundle.SigningPath, bundle.SigningPassword, SsoCertificateFormat.Pfx, X509KeyUsageFlags.DigitalSignature);
        using var encryption = SsoCertificateFile.Load(bundle.EncryptionPath, bundle.EncryptionPassword, SsoCertificateFormat.Pfx, X509KeyUsageFlags.KeyEncipherment);
        using var signingKey = signing.GetRSAPublicKey()!;
        using var encryptionKey = encryption.GetRSAPublicKey()!;
        if (signingKey.ExportSubjectPublicKeyInfo().AsSpan().SequenceEqual(encryptionKey.ExportSubjectPublicKeyInfo()))
            throw SsoCertificateFile.InvalidCredentials();
        VerifyBase64Export(bundle.SigningPath);
        VerifyBase64Export(bundle.EncryptionPath);
        if (File.ReadAllText(Path.Combine(directory, "render.env")) != RenderEnvironment(bundle.SigningPassword, bundle.EncryptionPassword))
            throw SsoCertificateFile.InvalidCredentials();
        return bundle;
    }

    private static string RenderEnvironment(string signingPassword, string encryptionPassword) =>
        "Sso__CertificateFormat=Base64Pfx\n" +
        "Sso__SigningCertificatePassword=" + signingPassword + "\n" +
        "Sso__EncryptionCertificatePassword=" + encryptionPassword + "\n";

    private static void VerifyBase64Export(string pfxPath)
    {
        byte[]? decoded = null;
        var original = File.ReadAllBytes(pfxPath);
        try
        {
            decoded = Convert.FromBase64String(File.ReadAllText(pfxPath + ".b64"));
            if (!CryptographicOperations.FixedTimeEquals(original, decoded)) throw SsoCertificateFile.InvalidCredentials();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(original);
            if (decoded is not null) CryptographicOperations.ZeroMemory(decoded);
        }
    }

    private static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw SsoCertificateFile.InvalidCredentials();
    }

    private static void PrivateDirectory(string path)
    {
        if (Directory.Exists(path)) { RejectLink(path); return; }
        if (OperatingSystem.IsWindows())
        {
            using var identity = WindowsIdentity.GetCurrent();
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(path).Create(security);
        }
        else Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static void WritePrivate(string directory, string name, byte[] bytes)
    {
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using var stream = new FileStream(Path.Combine(directory, name), options);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
