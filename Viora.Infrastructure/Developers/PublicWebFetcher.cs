using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Viora.Application.Developers;

namespace Viora.Infrastructure.Developers;

public static class PublicNetwork
{
    // Conservative special-use exclusions; false negatives are preferable to internal-network access.
    private static readonly (IPAddress Ip, int Bits)[] DeniedV4 = new[] {
        "0.0.0.0/8", "10.0.0.0/8", "100.64.0.0/10", "127.0.0.0/8", "169.254.0.0/16", "172.16.0.0/12",
        "192.0.0.0/24", "192.0.2.0/24", "192.31.196.0/24", "192.52.193.0/24", "192.88.99.0/24", "192.168.0.0/16",
        "192.175.48.0/24", "198.18.0.0/15", "198.51.100.0/24", "203.0.113.0/24", "224.0.0.0/4", "240.0.0.0/4"
    }.Select(Parse).ToArray();
    private static readonly (IPAddress Ip, int Bits)[] DeniedV6 = new[] { "2001::/23", "2001:db8::/32", "2002::/16", "2620:4f:8000::/48", "3fff::/20" }.Select(Parse).ToArray();
    private static (IPAddress, int) Parse(string cidr) { var p = cidr.Split('/'); return (IPAddress.Parse(p[0]), int.Parse(p[1])); }
    private static bool In(IPAddress ip, (IPAddress Ip, int Bits) range)
    {
        var bytes = ip.GetAddressBytes(); var prefix = range.Ip.GetAddressBytes();
        if (bytes.Length != prefix.Length) return false;
        for (int bit = 0; bit < range.Bits; bit++) if ((bytes[bit / 8] & (128 >> (bit % 8))) != (prefix[bit / 8] & (128 >> (bit % 8)))) return false;
        return true;
    }
    public static bool IsPublic(IPAddress ip) => ip.AddressFamily switch
    {
        AddressFamily.InterNetwork => !DeniedV4.Any(range => In(ip, range)),
        AddressFamily.InterNetworkV6 => !ip.IsIPv4MappedToIPv6 && ip.ScopeId == 0 && In(ip, (IPAddress.Parse("2000::"), 3)) && !DeniedV6.Any(range => In(ip, range)),
        _ => false,
    };
}

public interface IPublicDnsResolver { Task<IPAddress[]> ResolveAsync(string hostname, CancellationToken ct); }
public sealed class PublicDnsResolver : IPublicDnsResolver
{
    public Task<IPAddress[]> ResolveAsync(string hostname, CancellationToken ct) => Dns.GetHostAddressesAsync(hostname, ct);
}
public interface IPublicSocketConnector { ValueTask<Stream> ConnectAsync(IPAddress address, CancellationToken ct); }
public sealed class PublicSocketConnector : IPublicSocketConnector
{
    public async ValueTask<Stream> ConnectAsync(IPAddress address, CancellationToken ct)
    {
        if (!PublicNetwork.IsPublic(address)) throw new PublicFetchException("DOMAIN_BLOCKED", "Domain trỏ tới địa chỉ mạng không được phép.");
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try { await socket.ConnectAsync(new IPEndPoint(address, 443), ct); return new NetworkStream(socket, ownsSocket: true); }
        catch { socket.Dispose(); throw; }
    }
}

public sealed class PublicWebFetcher(IPublicDnsResolver resolver, IPublicSocketConnector connector) : IPublicWebFetcher, IPublicJsonFetcher
{
    // Tests may install a private test root CA on this handler only; production DI never supplies this hook.
    internal Action<SocketsHttpHandler>? ConfigureTestHandler { get; init; }
    internal TimeSpan Deadline { get; init; } = TimeSpan.FromSeconds(8);
    public Task<PublicFetchResult> FetchAsync(Uri uri,int maxBytes,bool verificationFile,CancellationToken ct)=>FetchCoreAsync(uri,maxBytes,verificationFile,false,ct);
    public Task<PublicFetchResult> FetchJsonAsync(Uri uri,int maxBytes,CancellationToken ct)=>FetchCoreAsync(uri,maxBytes,false,true,ct);
    private async Task<PublicFetchResult> FetchCoreAsync(Uri uri, int maxBytes, bool verificationFile,bool json, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(Deadline);
        var token = deadline.Token;
        try
        {
            try { uri = new Uri(PublicWebAddress.Normalize(uri.AbsoluteUri)); }
            catch (DeveloperAuthException) { throw new PublicFetchException("DOMAIN_BLOCKED", "Domain hoặc URL không hợp lệ để kiểm tra công khai."); }
            var addresses = (await resolver.ResolveAsync(uri.IdnHost, token).WaitAsync(token)).ToArray();
            if (addresses.Length is 0 or > 32 || addresses.Any(ip => !PublicNetwork.IsPublic(ip)))
                throw new PublicFetchException("DOMAIN_BLOCKED", "DNS trỏ tới địa chỉ mạng nội bộ, dành riêng hoặc không được phép.");
            using var handler = new SocketsHttpHandler {
                AllowAutoRedirect = false, UseProxy = false, UseCookies = false, Credentials = null, PreAuthenticate = false,
                AutomaticDecompression = DecompressionMethods.None, MaxResponseHeadersLength = 16, MaxConnectionsPerServer = 1,
                ConnectTimeout = Deadline, PooledConnectionLifetime = TimeSpan.Zero,
                ConnectCallback = async (context, cancel) => {
                    if (!context.DnsEndPoint.Host.Equals(uri.IdnHost, StringComparison.OrdinalIgnoreCase) || context.DnsEndPoint.Port != 443)
                        throw new PublicFetchException("DOMAIN_BLOCKED", "Kết nối không khớp domain được kiểm tra.");
                    // No second DNS lookup: connect only to an immutable copy of previously validated answers.
                    Exception? failure = null;
                    foreach (var address in addresses.Distinct()) {
                        try { return await connector.ConnectAsync(address, cancel); }
                        catch (SocketException e) { failure = e; }
                    }
                    throw new HttpRequestException("Connection failed", failure);
                },
            };
            // Certificate AIA/online revocation downloads must not bypass the pinned HTTP transport.
            handler.SslOptions.CertificateChainPolicy = new X509ChainPolicy {
                DisableCertificateDownloads = true, RevocationMode = X509RevocationMode.NoCheck,
                VerificationFlags = X509VerificationFlags.NoFlag,
                ApplicationPolicy = { new System.Security.Cryptography.Oid("1.3.6.1.5.5.7.3.1") },
            };
            ConfigureTestHandler?.Invoke(handler);
            using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            using var request = new HttpRequestMessage(HttpMethod.Get, uri) { Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(json ? "application/json" : verificationFile ? "text/plain" : "text/html"));
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is >= 300 and < 400) throw new PublicFetchException("REDIRECT_REJECTED", "Website chuyển hướng. Hãy dùng URL HTTPS truy cập trực tiếp.");
            if (response.StatusCode == HttpStatusCode.NotFound) throw new PublicFetchException("FILE_NOT_FOUND", "Không tìm thấy file hoặc trang chính sách (404).");
            if (response.StatusCode != HttpStatusCode.OK) throw new PublicFetchException("HTTP_ERROR", "Website chưa trả HTTP 200 công khai.");
            var media = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? "";
            var allowed = json ? media is "application/json" or "application/jwk-set+json" : verificationFile ? media == "text/plain" : media is "text/html" or "application/xhtml+xml" or "text/plain" or "application/pdf";
            if (!allowed || response.Content.Headers.ContentEncoding.Count != 0) throw new PublicFetchException("INVALID_CONTENT_TYPE", "Nội dung cần đúng định dạng và không nén.");
            if (response.Content.Headers.ContentLength > maxBytes) throw new PublicFetchException("RESPONSE_TOO_LARGE", "Nội dung website vượt giới hạn kiểm tra.");
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var output = new MemoryStream(); var buffer = new byte[4096]; int read;
            while ((read = await stream.ReadAsync(buffer, token)) > 0) {
                if (output.Length + read > maxBytes) throw new PublicFetchException("RESPONSE_TOO_LARGE", "Nội dung website vượt giới hạn kiểm tra.");
                await output.WriteAsync(buffer.AsMemory(0, read), token);
            }
            if (output.Length == 0) throw new PublicFetchException("EMPTY_CONTENT", "Website trả nội dung trống.");
            return new(output.ToArray(), media);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new PublicFetchException("FETCH_TIMEOUT", "Website phản hồi quá thời gian cho phép."); }
        catch (SocketException) { throw new PublicFetchException("DNS_ERROR", "Không phân giải được DNS hoặc kết nối domain."); }
        catch (HttpRequestException ex) when (ex.HttpRequestError == HttpRequestError.SecureConnectionError || ex.InnerException is AuthenticationException)
        { throw new PublicFetchException("TLS_ERROR", "Chứng chỉ HTTPS không hợp lệ hoặc kết nối TLS thất bại."); }
        catch (HttpRequestException) { throw new PublicFetchException("FETCH_FAILED", "Không thể kết nối HTTPS tới website."); }
        catch (IOException) { throw new PublicFetchException("FETCH_FAILED", "Không đọc được nội dung HTTPS từ website."); }
    }
}
