using System.Net;
using System.Net.Sockets;
using Viora.Application.MiniApps;

namespace Viora.Infrastructure.MiniApps;

public interface IMiniAppTechnicalProbe
{
    Task<MiniAppTechnicalCheckDto> CheckAsync(string code, string url, CancellationToken ct);
}

// Limited connectivity check, not a browser compatibility test or a production-isolated sandbox.
public sealed class MiniAppTechnicalProbe : IMiniAppTechnicalProbe
{
    public async Task<MiniAppTechnicalCheckDto> CheckAsync(string code, string url, CancellationToken ct)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !MiniAppSecurityPolicy.IsAllowedHttpsUrl(url, [uri.IdnHost]) || uri.Port != 443)
            return new(code, false, "Cần URL HTTPS công khai, không có thông tin đăng nhập hoặc cổng tùy chỉnh.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        using var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false, UseCookies = false, UseProxy = false,
            MaxResponseHeadersLength = 16, ConnectTimeout = TimeSpan.FromSeconds(5),
            ConnectCallback = async (context, token) =>
            {
                if (!context.DnsEndPoint.Host.Equals(uri.IdnHost, StringComparison.OrdinalIgnoreCase) || context.DnsEndPoint.Port != 443)
                    throw new HttpRequestException("Unexpected endpoint");
                var addresses = await Dns.GetHostAddressesAsync(uri.IdnHost, token);
                if (addresses.Length == 0 || addresses.Any(address => !MiniAppSecurityPolicy.IsPublicAddress(address)))
                    throw new HttpRequestException("Non-public DNS destination");
                var socket = new Socket(addresses[0].AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try { await socket.ConnectAsync(new IPEndPoint(addresses[0], 443), token); return new NetworkStream(socket, true); }
                catch { socket.Dispose(); throw; }
            }
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
        try
        {
            // Dispose after headers: no unbounded HTML body downloads or third-party scripts.
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            var passed = response.StatusCode == HttpStatusCode.OK;
            return new(code, passed, passed ? "URL trả HTTP 200 qua HTTPS; không chuyển hướng." : "URL cần trả HTTP 200 trực tiếp, không chuyển hướng.", (int)response.StatusCode, timer.ElapsedMilliseconds);
        }
        catch (Exception error) when (error is HttpRequestException or SocketException or OperationCanceledException or IOException)
        {
            ct.ThrowIfCancellationRequested();
            return new(code, false, "Không truy cập được URL HTTPS công khai trong thời gian cho phép.", null, timer.ElapsedMilliseconds);
        }
    }
}
