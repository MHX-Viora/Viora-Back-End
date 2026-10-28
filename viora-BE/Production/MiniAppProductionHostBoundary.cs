using System.Security.Claims;
using Viora.Infrastructure.Developers;

namespace viora_BE.Production;

public static class MiniAppProductionHostBoundary
{
    private static readonly HashSet<string> Paths = new(StringComparer.Ordinal)
    {
        "/mini-app-host", "/mini-app-host/assets/host.mjs", "/mini-app-host/assets/bridge.mjs",
        "/mini-app-host/assets/navigation.mjs", "/mini-app-host/assets/native.mjs", "/mini-app-host/assets/host.css",
        "/api/mini-app-host/prepare", "/api/mini-app-host/redeem", "/api/mini-app-host/context",
        "/api/mini-app-host/heartbeat", "/api/mini-app-host/close", "/api/mini-app-host/events", "/api/mini-app-host/external-link"
    };
    public static void UseMiniAppProductionHostBoundary(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<MiniAppProductionOptions>();
        if (!Uri.TryCreate(options.HostOrigin, UriKind.Absolute, out var host) || host.Scheme != "https" || options.IsReservedHost(host.IdnHost)) return;
        app.Use(async (http, next) =>
        {
            string requestHost;
            try
            {
                if (!Uri.TryCreate("https://" + http.Request.Host.Value, UriKind.Absolute, out var parsed)) { Deny(http); return; }
                requestHost = parsed.IdnHost.TrimEnd('.');
            }
            catch (UriFormatException) { Deny(http); return; }
            if (!requestHost.Equals(host.IdnHost.TrimEnd('.'), StringComparison.OrdinalIgnoreCase)) { await next(); return; }
            var path = http.Request.Path.Value ?? ""; const string run = "/mini-app-host/run/";
            var allowed = Paths.Contains(path) || path.StartsWith(run, StringComparison.Ordinal) &&
                Guid.TryParseExact(path[run.Length..], "D", out var session) && session != Guid.Empty;
            // Keep hostname isolation during rollback as well as on wrong ports,
            // schemes and dotted host aliases. No privileged routes live here.
            if (!options.Enabled || !http.Request.IsHttps || !http.Request.Host.Value.Equals(host.Authority, StringComparison.OrdinalIgnoreCase) || !allowed)
            { Deny(http); return; }
            http.Request.Headers.Remove("Authorization");
            http.User = new ClaimsPrincipal(new ClaimsIdentity());
            await next();
        });
    }
    private static void Deny(HttpContext http)
    {
        http.Response.StatusCode = 404; http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers["Referrer-Policy"] = "no-referrer";
        http.Response.Headers["X-Content-Type-Options"] = "nosniff";
        http.Response.Headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
    }
}
