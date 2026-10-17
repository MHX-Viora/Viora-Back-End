using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

internal sealed class FixtureCertificates : IDisposable
{
    public X509Certificate2 Tls {get;}
    public string SigningPath {get;}
    public string EncryptionPath {get;}
    public FixtureCertificates(string directory)
    {
        var now=DateTimeOffset.UtcNow;
        using var rootKey=RSA.Create(2048);
        var ca=new CertificateRequest("CN=ANKT isolated OIDC browser fixture",rootKey,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        ca.CertificateExtensions.Add(new X509BasicConstraintsExtension(true,false,0,true));
        ca.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign,true));
        using var root=ca.CreateSelfSigned(now.AddMinutes(-5),now.AddHours(3));
        File.WriteAllText(Path.Combine(directory,"root-ca.pem"),root.ExportCertificatePem());
        using var key=RSA.Create(2048);
        var leaf=new CertificateRequest("CN=localhost",key,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        var san=new SubjectAlternativeNameBuilder();san.AddDnsName("localhost");san.AddIpAddress(IPAddress.Loopback);san.AddIpAddress(IPAddress.Parse("127.0.0.2"));
        leaf.CertificateExtensions.Add(san.Build());
        leaf.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature|X509KeyUsageFlags.KeyEncipherment,true));
        leaf.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection{new("1.3.6.1.5.5.7.3.1")},true));
        using var signed=leaf.Create(root,now.AddMinutes(-2),now.AddHours(2),RandomNumberGenerator.GetBytes(16));
        using var keyed=signed.CopyWithPrivateKey(key);
        Tls=new(keyed.Export(X509ContentType.Pfx),(string?)null,X509KeyStorageFlags.Exportable);
        File.WriteAllText(Path.Combine(directory,"tls-cert.pem"),keyed.ExportCertificatePem());
        File.WriteAllText(Path.Combine(directory,"tls-key.pem"),key.ExportPkcs8PrivateKeyPem());
        SigningPath=WritePurpose(directory,"signing",X509KeyUsageFlags.DigitalSignature);
        EncryptionPath=WritePurpose(directory,"encryption",X509KeyUsageFlags.KeyEncipherment);
    }
    private static string WritePurpose(string directory,string purpose,X509KeyUsageFlags usage)
    {
        using var key=RSA.Create(2048);var request=new CertificateRequest("CN=ANKT isolated "+purpose,key,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(usage,true));
        using var certificate=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5),DateTimeOffset.UtcNow.AddHours(2));
        var path=Path.Combine(directory,purpose+".pfx");File.WriteAllBytes(path,certificate.Export(X509ContentType.Pfx));return path;
    }
    public void Dispose()=>Tls.Dispose();
}
