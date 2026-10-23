using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Viora.Infrastructure.Developers;

namespace viora_BE.Production;

public static class MiniAppProductionHostDocuments
{
    private static readonly Lazy<string> Script = new(() => Embedded("Ankt.ProductionHost.Script"));
    private static readonly Lazy<string> Style = new(() => Embedded("Ankt.ProductionHost.Style"));
    private static readonly Lazy<string> Bridge = new(() => Embedded("Ankt.ProductionHost.Bridge"));
    private static readonly Lazy<string> Navigation = new(() => Embedded("Ankt.ProductionHost.Navigation"));
    private static readonly Lazy<string> Native = new(() => Embedded("Ankt.ProductionHost.Native"));
    public static void MapMiniAppProductionHostDocuments(this IEndpointRouteBuilder endpoints)
    {
        if (!endpoints.ServiceProvider.GetRequiredService<IOptions<MiniAppProductionOptions>>().Value.Enabled) return;
        var group = endpoints.MapGroup("/mini-app-host").AllowAnonymous().RequireRateLimiting("mini-app-host-runtime")
            .AddEndpointFilter<MiniAppProductionHostFilter>();
        group.AddEndpointFilter(async (context, next) =>
        {
            var options = context.HttpContext.RequestServices.GetRequiredService<MiniAppProductionOptions>();
            Csp(context.HttpContext, options, "'none'"); return await next(context);
        });
        group.MapGet("", (MiniAppProductionOptions options) => Results.Content(Document(new
            { mode = "prepare", parentOrigin = options.FirstPartyOrigin, hostOrigin = options.HostOrigin }), "text/html; charset=utf-8"));
        group.MapGet("/assets/host.mjs", () => Results.Text(Script.Value, "text/javascript; charset=utf-8"));
        group.MapGet("/assets/host.css", () => Results.Text(Style.Value, "text/css; charset=utf-8"));
        group.MapGet("/assets/bridge.mjs", () => Results.Text(Bridge.Value, "text/javascript; charset=utf-8"));
        group.MapGet("/assets/navigation.mjs", () => Results.Text(Navigation.Value, "text/javascript; charset=utf-8"));
        group.MapGet("/assets/native.mjs", () => Results.Text(Native.Value, "text/javascript; charset=utf-8"));
        group.MapGet("/run/{sessionId:guid}", async (Guid sessionId, HttpContext http, MiniAppProductionRuntimeService service, MiniAppProductionOptions options, CancellationToken ct) =>
        {
            var data = await service.DocumentAsync(sessionId, MiniAppProductionHostApi.UniqueCookie(http, MiniAppProductionHostApi.RuntimeCookie(sessionId)),
                MiniAppProductionHostApi.UniqueCookie(http, MiniAppProductionHostApi.BindingCookie), ct);
            Csp(http, options, data.PartnerOrigin);
            return Results.Content(Document(new { mode = "run", parentOrigin = options.FirstPartyOrigin, hostOrigin = options.HostOrigin,
                data.SessionId, data.AppId, data.Name, data.WebsiteUrl, data.PartnerOrigin, data.SsoEnabled, data.ExpiresAt }), "text/html; charset=utf-8");
        });
    }
    private static void Csp(HttpContext http, MiniAppProductionOptions options, string frameSource) =>
        http.Response.Headers["Content-Security-Policy"] = "default-src 'none'; script-src 'self'; style-src 'self'; connect-src 'self'; img-src data:; object-src 'none'; base-uri 'none'; form-action 'none'; frame-src " + frameSource + "; frame-ancestors " + options.FirstPartyOrigin + ";";
    private static string Document(object config)
    {
        var json = WebUtility.HtmlEncode(JsonSerializer.Serialize(config, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return "<!doctype html><html lang=vi><head><meta charset=utf-8><meta name=viewport content=\"width=device-width,initial-scale=1\"><title>Mini App · ANKT</title><link rel=icon href=\"data:,\"><link rel=stylesheet href=\"/mini-app-host/assets/host.css\"></head>" +
            "<body><main id=host data-config=\"" + json + "\"><header><h1 id=app-name>Mini App</h1><button id=external type=button disabled aria-label=\"Mở website ở tab mới\">Mở ngoài</button><button id=close type=button aria-label=\"Đóng Mini App\">Đóng</button></header>" +
            "<p id=status role=status aria-live=polite>Đang chuẩn bị Mini App…</p><section id=preview aria-label=\"Nội dung Mini App\"></section></main><script type=module src=\"/mini-app-host/assets/host.mjs\"></script></body></html>";
    }
    private static string Embedded(string name)
    {
        using var stream = typeof(MiniAppProductionHostDocuments).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException("Production host resource unavailable.");
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
}
