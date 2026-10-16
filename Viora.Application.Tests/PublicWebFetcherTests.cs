using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Formats.Asn1;
using System.Text;
using Viora.Application.Developers;
using Viora.Infrastructure.Developers;
using Xunit;

public sealed class PublicWebFetcherTests
{
    [Fact]
    public async Task CertificateAiaCannotFetchInternalAddressesOutsidePinnedTransport()
    {
        var trap=new TcpListener(IPAddress.Loopback,0); trap.Start();
        try {
            var port=((IPEndPoint)trap.LocalEndpoint).Port;
            await using var site=new ControlledHttpsSite("proof",aiaUrl:$"http://127.0.0.1:{port}/{Guid.NewGuid():N}-issuer.cer");
            // A non-Schannel server isolates client AIA behavior from Windows server credential acquisition.
            await using var python=await PythonTlsServer.Start(site.Certificate,site.PrivateKeyPem);
            var fetcher=Create(site,new(),new(python.Port));
            Assert.Equal("TLS_ERROR",(await Assert.ThrowsAsync<PublicFetchException>(()=>fetcher.FetchAsync(new("https://partner.example.com/file"),4096,true,default))).Code);
            Assert.False(trap.Pending());
        } finally {trap.Stop();}
    }
    [Fact]
    public async Task ControlledHttpsUsesPinnedAddressNormalTlsAndNoCredentials()
    {
        await using var site = new ControlledHttpsSite("proof");
        var resolver = new Resolver(); var connector = new Connector(site.Port);
        var fetcher = Create(site, resolver, connector);
        PublicFetchResult result;
        try { result = await fetcher.FetchAsync(new("https://partner.example.com/ankt-verify-test.txt"), 4096, true, default); }
        catch (PublicFetchException) { throw new Xunit.Sdk.XunitException("Controlled TLS server failure: " + site.Failure); }
        Assert.Equal("proof", Encoding.UTF8.GetString(result.Content)); Assert.Equal(1, resolver.Calls);
        Assert.Equal(IPAddress.Parse("8.8.8.8"), Assert.Single(connector.Addresses));
        Assert.Contains("GET /ankt-verify-test.txt HTTP/1.1", site.Request);
        Assert.Contains("Host: partner.example.com", site.Request);
        Assert.DoesNotContain("Authorization:", site.Request); Assert.DoesNotContain("Cookie:", site.Request);
        // A hypothetical second DNS lookup would return loopback; the pinned connection never asks again.
    }
    [Theory]
    [InlineData("127.0.0.1")][InlineData("169.254.169.254")][InlineData("::1")][InlineData("fd00::1")]
    public async Task MixedDnsRejectsAllConnections(string bad)
    {
        var dns = new Resolver { Answers = [IPAddress.Parse("8.8.8.8"), IPAddress.Parse(bad)] }; var connector = new Connector(1);
        var error = await Assert.ThrowsAsync<PublicFetchException>(() => new PublicWebFetcher(dns, connector).FetchAsync(new("https://partner.example.com/file"), 4096, true, default));
        Assert.Equal("DOMAIN_BLOCKED", error.Code); Assert.Empty(connector.Addresses);
    }
    [Fact]
    public async Task DnsIsWithinTotalDeadline()
    {
        var dns = new Resolver { Stall = true }; var connector = new Connector(1);
        var fetcher = new PublicWebFetcher(dns, connector) { Deadline = TimeSpan.FromMilliseconds(100) };
        var error = await Assert.ThrowsAsync<PublicFetchException>(() => fetcher.FetchAsync(new("https://partner.example.com/file"), 4096, true, default));
        Assert.Equal("FETCH_TIMEOUT", error.Code); Assert.Empty(connector.Addresses);
    }
    [Theory]
    [InlineData(404, "text/plain", "missing", "FILE_NOT_FOUND")]
    [InlineData(302, "text/plain", "redirect", "REDIRECT_REJECTED")]
    [InlineData(200, "text/html", "wrong media", "INVALID_CONTENT_TYPE")]
    [InlineData(200, "text/plain", "", "EMPTY_CONTENT")]
    public async Task HttpResponsesAreValidated(int status, string media, string body, string code)
    {
        await using var site = new ControlledHttpsSite(body, status: status, contentType: media);
        var fetcher = Create(site, new(), new(site.Port));
        Assert.Equal(code, (await Assert.ThrowsAsync<PublicFetchException>(() => fetcher.FetchAsync(new("https://partner.example.com/file"), 4096, true, default))).Code);
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task ContentLengthAndStreamAreBothBounded(bool omitLength)
    {
        await using var site = new ControlledHttpsSite(new string('x', 5000), omitLength: omitLength);
        Assert.Equal("RESPONSE_TOO_LARGE", (await Assert.ThrowsAsync<PublicFetchException>(() => Create(site, new(), new(site.Port)).FetchAsync(new("https://partner.example.com/file"), 4096, true, default))).Code);
    }
    [Theory]
    [InlineData(false, "partner.example.com")]
    [InlineData(true, "different.example.com")]
    public async Task UntrustedAndWrongHostnameCertificatesAreRejected(bool trusted, string certificateHost)
    {
        await using var site = new ControlledHttpsSite("proof", certificateHost: certificateHost);
        var fetcher = trusted ? Create(site, new(), new(site.Port)) : new PublicWebFetcher(new Resolver(), new Connector(site.Port));
        Assert.Equal("TLS_ERROR", (await Assert.ThrowsAsync<PublicFetchException>(() => fetcher.FetchAsync(new("https://partner.example.com/file"), 4096, true, default))).Code);
    }
    [Fact]
    public async Task SlowHeadersAndBodyAreBoundedByTotalDeadline()
    {
        await using var site = new ControlledHttpsSite("proof", delay: TimeSpan.FromSeconds(5));
        var fetcher = Create(site, new(), new(site.Port), TimeSpan.FromMilliseconds(300));
        Assert.Equal("FETCH_TIMEOUT", (await Assert.ThrowsAsync<PublicFetchException>(() => fetcher.FetchAsync(new("https://partner.example.com/file"), 4096, true, default))).Code);
    }
    internal static PublicWebFetcher Create(ControlledHttpsSite site, Resolver dns, Connector connector, TimeSpan? timeout = null) => new(dns, connector) {
        Deadline = timeout ?? TimeSpan.FromSeconds(8),
        ConfigureTestHandler = handler => {
            Assert.True(handler.SslOptions.CertificateChainPolicy!.DisableCertificateDownloads);
            handler.SslOptions.CertificateChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            handler.SslOptions.CertificateChainPolicy.CustomTrustStore.Add(site.Root);
        },
    };
    internal sealed class Resolver : IPublicDnsResolver
    {
        public int Calls; public bool Stall; public IPAddress[] Answers = [IPAddress.Parse("8.8.8.8")];
        public async Task<IPAddress[]> ResolveAsync(string hostname, CancellationToken ct) {
            Calls++; if (Stall) await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return Calls == 1 ? Answers : [IPAddress.Loopback];
        }
    }
    internal sealed class Connector(int port) : IPublicSocketConnector
    {
        public readonly List<IPAddress> Addresses = [];
        public async ValueTask<Stream> ConnectAsync(IPAddress address, CancellationToken ct) {
            Addresses.Add(address); var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try { await client.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port), ct); return new NetworkStream(client, true); }
            catch { client.Dispose(); throw; }
        }
    }
}

// Test transport only: a private root CA scoped to one handler; no machine trust-store writes or production bypass.
internal sealed class ControlledHttpsSite : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task server; private readonly X509Certificate2 leaf;
    public X509Certificate2 Root { get; }
    public X509Certificate2 Certificate => leaf;
    public string PrivateKeyPem {get;}
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    public string Request { get; private set; } = "";
    public Exception? Failure { get; private set; }
    public ControlledHttpsSite(string body, int status = 200, string contentType = "text/plain", bool omitLength = false, string certificateHost = "partner.example.com", TimeSpan? delay = null, string? aiaUrl = null)
    {
        using var rootKey = RSA.Create(2048); using var leafKey = RSA.Create(2048);
        PrivateKeyPem=leafKey.ExportPkcs8PrivateKeyPem();
        var rootRequest = new CertificateRequest("CN=ANKT controlled test root", rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        Root = rootRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(1));
        var leafRequest = new CertificateRequest("CN=" + certificateHost, leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder(); san.AddDnsName(certificateHost); leafRequest.CertificateExtensions.Add(san.Build());
        leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        leafRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        using var intermediateKey=RSA.Create(2048); X509Certificate2? intermediate=null;
        if(aiaUrl is not null) {
            var ca=new CertificateRequest("CN=Missing test intermediate",intermediateKey,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
            ca.CertificateExtensions.Add(new X509BasicConstraintsExtension(true,false,0,true)); ca.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign,true));
            using var publicCa=ca.Create(Root,DateTimeOffset.UtcNow.AddMinutes(-10),DateTimeOffset.UtcNow.AddHours(2),RandomNumberGenerator.GetBytes(16));
            intermediate=publicCa.CopyWithPrivateKey(intermediateKey);
            var aia=new AsnWriter(AsnEncodingRules.DER); aia.PushSequence(); aia.PushSequence(); aia.WriteObjectIdentifier("1.3.6.1.5.5.7.48.2");
            aia.WriteCharacterString(UniversalTagNumber.IA5String,aiaUrl,new Asn1Tag(TagClass.ContextSpecific,6)); aia.PopSequence(); aia.PopSequence();
            leafRequest.CertificateExtensions.Add(new X509Extension("1.3.6.1.5.5.7.1.1",aia.Encode(),false));
        }
        using var signed = leafRequest.Create(intermediate ?? Root, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1), RandomNumberGenerator.GetBytes(16));
        intermediate?.Dispose();
        using var keyed = signed.CopyWithPrivateKey(leafKey);
        // Windows Schannel requires an imported key handle; default import removes its temporary key on disposal.
        leaf = new X509Certificate2(keyed.Export(X509ContentType.Pfx), (string?)null, X509KeyStorageFlags.Exportable);
        listener.Start();
        server = Task.Run(async () => {
            try {
                using var client = await listener.AcceptTcpClientAsync(lifetime.Token);
                await using var tls = new SslStream(client.GetStream());
                await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificateContext = SslStreamCertificateContext.Create(leaf,additionalCertificates:null,offline:true), EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 }, lifetime.Token);
                var buffer = new byte[1]; var header = new StringBuilder();
                while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal) && header.Length < 16384) {
                    if (await tls.ReadAsync(buffer, lifetime.Token) == 0) return; header.Append((char)buffer[0]);
                }
                Request = header.ToString();
                if (delay is not null) await Task.Delay(delay.Value, lifetime.Token);
                var payload = Encoding.UTF8.GetBytes(body);
                var headers = $"HTTP/1.1 {status} Test\r\nContent-Type: {contentType}\r\nConnection: close\r\n" + (omitLength ? "" : $"Content-Length: {payload.Length}\r\n") + (status == 302 ? "Location: https://127.0.0.1/metadata\r\n" : "") + "\r\n";
                await tls.WriteAsync(Encoding.ASCII.GetBytes(headers), lifetime.Token); await tls.WriteAsync(payload, lifetime.Token); await tls.FlushAsync(lifetime.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or AuthenticationException or SocketException) { Failure = ex; }
        });
    }
    public async ValueTask DisposeAsync() { lifetime.Cancel(); listener.Stop(); await server; leaf.Dispose(); Root.Dispose(); lifetime.Dispose(); }
}

internal sealed class PythonTlsServer : IAsyncDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"ankt-tls-tests");
    private readonly string directory;
    private readonly System.Diagnostics.Process process;
    public int Port {get;private set;}
    private PythonTlsServer(X509Certificate2 certificate,string privateKeyPem)
    {
        directory=Path.Combine(root,Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var certPath=Path.Combine(directory,"leaf.pem"); var keyPath=Path.Combine(directory,"key.pem"); var script=Path.Combine(directory,"server.py");
        File.WriteAllText(certPath,certificate.ExportCertificatePem());
        File.WriteAllText(keyPath,privateKeyPem);
        File.WriteAllText(script,"""
import socket, ssl, sys
context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
context.load_cert_chain(sys.argv[1], sys.argv[2])
with socket.socket() as listener:
    listener.bind(('127.0.0.1', 0))
    listener.listen(1)
    listener.settimeout(10)
    print(listener.getsockname()[1], flush=True)
    connection, _ = listener.accept()
    try:
        with context.wrap_socket(connection, server_side=True) as stream:
            request = b''
            while b'\r\n\r\n' not in request and len(request) < 16384:
                chunk = stream.recv(4096)
                if not chunk: break
                request += chunk
            stream.sendall(b'HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: 5\r\nConnection: close\r\n\r\nproof')
    except (ssl.SSLError, OSError):
        connection.close()
""");
        var start=new System.Diagnostics.ProcessStartInfo("python") {UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
        start.ArgumentList.Add(script); start.ArgumentList.Add(certPath); start.ArgumentList.Add(keyPath);
        process=System.Diagnostics.Process.Start(start)??throw new InvalidOperationException("Python TLS fixture did not start.");
    }
    public static async Task<PythonTlsServer> Start(X509Certificate2 certificate,string privateKeyPem)
    {
        var server=new PythonTlsServer(certificate,privateKeyPem);
        try { var port=await server.process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)); server.Port=int.Parse(port??throw new InvalidOperationException("Python TLS fixture exited.")); return server; }
        catch {await server.DisposeAsync();throw;}
    }
    public async ValueTask DisposeAsync()
    {
        if(!process.HasExited)process.Kill(entireProcessTree:true); await process.WaitForExitAsync(); process.Dispose();
        var resolved=Path.GetFullPath(directory); var allowed=Path.GetFullPath(root)+Path.DirectorySeparatorChar;
        if(!resolved.StartsWith(allowed,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Unexpected TLS fixture path.");
        Directory.Delete(resolved,recursive:true);
    }
}
