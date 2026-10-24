using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace viora_BE.Hosting;

public static class TrustedProxyHeaders
{
    public static IServiceCollection AddTrustedProxyHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        var addresses = configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [];
        var ranges = configuration.GetSection("ReverseProxy:KnownNetworks").Get<string[]>() ?? [];
        var proxies = new List<IPAddress> { IPAddress.Loopback, IPAddress.IPv6Loopback };
        var networks = new List<Microsoft.AspNetCore.HttpOverrides.IPNetwork>();
        foreach (var address in addresses)
        {
            if (!IPAddress.TryParse(address, out var ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any))
                throw new InvalidOperationException("ReverseProxy:KnownProxies must contain explicit proxy IP addresses.");
            proxies.Add(ip);
            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) proxies.Add(ip.MapToIPv6());
        }
        foreach (var range in ranges)
        {
            if (!System.Net.IPNetwork.TryParse(range, out var network) || network.PrefixLength == 0)
                throw new InvalidOperationException("ReverseProxy:KnownNetworks must contain bounded, confirmed proxy CIDR ranges; /0 is not allowed.");
            networks.Add(new(network.BaseAddress, network.PrefixLength));
        }
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            // Scheme only: preserve Host and peer IP used by existing boundaries/rate limits.
            options.ForwardedHeaders = ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownProxies.Clear(); options.KnownNetworks.Clear();
            foreach (var proxy in proxies) options.KnownProxies.Add(proxy);
            foreach (var network in networks) options.KnownNetworks.Add(network);
        });
        return services;
    }

    public static IApplicationBuilder UseTrustedProxyHeaders(this IApplicationBuilder app)
    {
        app.UseForwardedHeaders();
        var logger = app.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger("ANKT.ReverseProxy");
        var reported = 0;
        return app.Use(async (context, next) =>
        {
            if (!context.Request.IsHttps && context.Request.Path == "/.well-known/openid-configuration" &&
                context.Request.Headers.ContainsKey("X-Forwarded-Proto") && Interlocked.Exchange(ref reported, 1) == 0)
            {
                // One diagnostic per process: peer IP only; no headers, query, cookies or tokens.
                logger.LogWarning("SSO discovery still sees HTTP behind a proxy. Immediate peer: {ProxyPeer}. Confirm this peer belongs to your ingress, then configure ReverseProxy__KnownProxies__0 or its confirmed KnownNetworks range. HTTPS enforcement remains enabled.",
                    context.Connection.RemoteIpAddress);
            }
            await next(context);
        });
    }
}
