using System.Net;
using System.Net.Sockets;
using System.Text;
using Viora.Application.MiniApps;

namespace Viora.Infrastructure.MiniApps;

// The connection callback validates every DNS answer and pins the actual socket to
// one validated address, eliminating a second DNS lookup and rebinding gap.
public sealed class MiniAppDomainVerifier(Func<HttpMessageHandler>? handlerFactory = null) : IMiniAppDomainVerifier
{
    public async Task<bool> VerifyAsync(string host, string challengeToken, CancellationToken cancellationToken)
        => (await CheckAsync(host, "ankt-mini-app-verification.txt", challengeToken, cancellationToken)).Verified;

    public async Task<MiniAppDomainCheck> CheckAsync(string host, string fileName, string content, CancellationToken cancellationToken)
    {
        if (!MiniAppSecurityPolicy.IsPublicHost(host) || !(fileName == "ankt-mini-app-verification.txt" ||
            System.Text.RegularExpressions.Regex.IsMatch(fileName, "^ankt-verify-[a-f0-9]{32}\\.txt$")))
            return new(false, "DOMAIN_INVALID", "Domain hoặc tên file xác minh không hợp lệ.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        using var handler = handlerFactory?.Invoke() ?? new SocketsHttpHandler
        {
            AllowAutoRedirect = false, UseProxy = false, UseCookies = false,
            AutomaticDecompression = DecompressionMethods.None, MaxResponseHeadersLength = 16,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            ConnectCallback = async (context, ct) =>
            {
                if (!context.DnsEndPoint.Host.Equals(host, StringComparison.OrdinalIgnoreCase) || context.DnsEndPoint.Port != 443)
                    throw new HttpRequestException("Unregistered verification endpoint.");
                var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
                if (addresses.Length == 0 || addresses.Any(a => !MiniAppSecurityPolicy.IsPublicAddress(a)))
                    throw new HttpRequestException("Domain resolves to a non-public address.");
                var socket = new Socket(addresses[0].AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try { await socket.ConnectAsync(new IPEndPoint(addresses[0], 443), ct); return new NetworkStream(socket, ownsSocket: true); }
                catch { socket.Dispose(); throw; }
            }
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
        try
        {
            using var response = await client.GetAsync($"https://{host}/{fileName}", HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (response.StatusCode == HttpStatusCode.NotFound) return new(false, "DOMAIN_FILE_NOT_FOUND", "Không tìm thấy file (HTTP 404). Đặt file vào thư mục public gốc rồi thử lại.");
            if ((int)response.StatusCode is >= 300 and < 400) return new(false, "DOMAIN_REDIRECT_REJECTED", "File đang chuyển hướng. URL xác minh phải trả HTTP 200 trực tiếp.");
            if (response.StatusCode != HttpStatusCode.OK) return new(false, "DOMAIN_HTTP_ERROR", $"Website trả HTTP {(int)response.StatusCode}; cần HTTP 200.");
            if (response.Content.Headers.ContentLength > 1024) return new(false, "DOMAIN_FILE_TOO_LARGE", "File xác minh lớn hơn 1 KB.");
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            var buffer = new byte[1025]; var count = 0;
            while (count < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(count), deadline.Token);
                if (read == 0) break; count += read;
            }
            if (count > 1024) return new(false, "DOMAIN_FILE_TOO_LARGE", "File xác minh lớn hơn 1 KB.");
            return Encoding.UTF8.GetString(buffer, 0, count).Trim().Equals(content, StringComparison.Ordinal)
                ? new(true) : new(false, "DOMAIN_CONTENT_MISMATCH", "Nội dung file không khớp. Tải và đặt đúng file mới nhất, giữ nguyên nội dung.");
        }
        catch (Exception e) when (e is HttpRequestException or SocketException or OperationCanceledException or IOException)
        { if (cancellationToken.IsCancellationRequested) throw; return new(false, e is OperationCanceledException ? "DOMAIN_TIMEOUT" : "DOMAIN_HTTPS_ERROR", "Website không phản hồi hoặc HTTPS/DNS không hợp lệ. Kiểm tra chứng chỉ và đường dẫn file."); }
    }
}
