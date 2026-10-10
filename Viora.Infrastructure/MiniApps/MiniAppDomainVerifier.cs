using System.Net;
using System.Net.Sockets;
using System.Text;
using Viora.Application.MiniApps;

namespace Viora.Infrastructure.MiniApps;

// The connection callback validates every DNS answer and pins the actual socket to
// one validated address, eliminating a second DNS lookup and rebinding gap.
public sealed class MiniAppDomainVerifier : IMiniAppDomainVerifier
{
    public async Task<bool> VerifyAsync(string host, string challengeToken, CancellationToken cancellationToken)
    {
        if (!MiniAppSecurityPolicy.IsPublicHost(host)) return false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        using var handler = new SocketsHttpHandler
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
            using var response = await client.GetAsync($"https://{host}/.well-known/ankt-mini-app-verification.txt", HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentLength > 1024) return false;
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            var buffer = new byte[1025]; var count = 0;
            while (count < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(count), deadline.Token);
                if (read == 0) break; count += read;
            }
            return count <= 1024 && Encoding.UTF8.GetString(buffer, 0, count).Trim().Equals(challengeToken, StringComparison.Ordinal);
        }
        catch (Exception e) when (e is HttpRequestException or SocketException or OperationCanceledException or IOException)
        { if (cancellationToken.IsCancellationRequested) throw; return false; }
    }
}
