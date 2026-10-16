using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.RegularExpressions;
using Viora.Infrastructure.Developers;

// A loopback-only real HTTPS website for tests. Production never registers this resolver, connector or test root.
internal sealed class ControlledDomainSite(WebApplication site, X509Certificate2 root, X509Certificate2 certificate, string directory, int port) : IAsyncDisposable, IPublicDnsResolver, IPublicSocketConnector
{
    public const string Hostname="miniapp.ankt-e2e.example.com";
    public const string AlternateHostname="changed.ankt-e2e.example.com";
    public static async Task<ControlledDomainSite> Start()
    {
        var baseDirectory=Path.Combine(Path.GetTempPath(),"ankt-domain-e2e");
        var directory=Path.Combine(baseDirectory,Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        using var rootKey=RSA.Create(2048); using var key=RSA.Create(2048);
        var caRequest=new CertificateRequest("CN=ANKT isolated E2E root",rootKey,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true,false,0,true)); caRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign,true));
        var root=caRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddHours(-1),DateTimeOffset.UtcNow.AddDays(1));
        var request=new CertificateRequest("CN="+Hostname,key,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        var san=new SubjectAlternativeNameBuilder(); san.AddDnsName(Hostname);san.AddDnsName(AlternateHostname); request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false,false,0,true)); request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection{new("1.3.6.1.5.5.7.3.1")},true));
        using var signed=request.Create(root,DateTimeOffset.UtcNow.AddMinutes(-5),DateTimeOffset.UtcNow.AddHours(1),RandomNumberGenerator.GetBytes(16)); using var keyed=signed.CopyWithPrivateKey(key);
        var certificate=new X509Certificate2(keyed.Export(X509ContentType.Pfx),(string?)null,X509KeyStorageFlags.Exportable);
        var b=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development",Args=[]}); b.Configuration.Sources.Clear();b.Logging.ClearProviders();
        b.WebHost.ConfigureKestrel(o=>o.Listen(IPAddress.Loopback,0,listen=>listen.UseHttps(certificate)));
        var site=b.Build();
        site.MapGet("/privacy",()=>Results.Content("<h1>Controlled test privacy policy</h1>","text/html"));
        site.MapGet("/terms",()=>Results.Content("<h1>Controlled test terms</h1>","text/html"));
        site.MapGet("/{filename}",(string filename)=>{
            if(!Regex.IsMatch(filename,"^ankt-verify-[a-f0-9]{32}\\.txt$",RegexOptions.CultureInvariant))return Results.NotFound();
            var file=Path.Combine(directory,filename);
            return File.Exists(file)?Results.File(file,"text/plain"):Results.NotFound();
        });
        await site.StartAsync(); var port=new Uri(site.Urls.Single()).Port;
        await File.WriteAllTextAsync(Path.Combine(directory,"root-ca.pem"),root.ExportCertificatePem());
        await File.WriteAllTextAsync(Path.Combine(directory,"fixture.json"),JsonSerializer.Serialize(new{hostname=Hostname,alternateHostname=AlternateHostname,port}));
        return new(site,root,certificate,directory,port);
    }
    public PublicWebFetcher Fetcher()=>new(this,this){ConfigureTestHandler=handler=>{
        handler.SslOptions.CertificateChainPolicy!.TrustMode=X509ChainTrustMode.CustomRootTrust;
        handler.SslOptions.CertificateChainPolicy.CustomTrustStore.Add(root);
    }};
    public Task<IPAddress[]> ResolveAsync(string hostname,CancellationToken ct)
    {
        if(hostname is not(Hostname or AlternateHostname))throw new SocketException((int)SocketError.HostNotFound);
        return Task.FromResult(new[]{IPAddress.Parse("8.8.8.8")});
    }
    public async ValueTask<Stream> ConnectAsync(IPAddress address,CancellationToken ct)
    {
        if(!address.Equals(IPAddress.Parse("8.8.8.8")))throw new SocketException((int)SocketError.AccessDenied);
        var socket=new Socket(AddressFamily.InterNetwork,SocketType.Stream,ProtocolType.Tcp);
        try{await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback,port),ct);return new NetworkStream(socket,true);}catch{socket.Dispose();throw;}
    }
    public async ValueTask DisposeAsync()
    {
        await site.StopAsync(); await site.DisposeAsync(); certificate.Dispose();root.Dispose();
        var allowed=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"ankt-domain-e2e"))+Path.DirectorySeparatorChar;
        var resolved=Path.GetFullPath(directory);if(!resolved.StartsWith(allowed,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Unexpected fixture directory.");
        Directory.Delete(resolved,recursive:true);
    }
}
