using Viora.Infrastructure.OpenApi;
using Viora.Infrastructure.Persistence;
using OpenIddict.Abstractions;
using System.Text.Json;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;

namespace viora_BE.OpenApi;

public static class PartnerOpenApiBoundary
{
    public static IApplicationBuilder UsePartnerOpenApiBoundary(this WebApplication app, PartnerOpenApiOptions settings) => app.Use(async (context, next) =>
    {
        var path = context.Request.Path.Value ?? "/";
        var publicHost = Uri.TryCreate(settings.Issuer, UriKind.Absolute, out var issuer) && context.Request.Host.Value.Equals(issuer.Authority, StringComparison.OrdinalIgnoreCase);
        if (publicHost && !settings.Enabled) { context.Response.StatusCode = 404; return; }
        var protocolPath = path.StartsWith("/oauth/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/oidc/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/v1/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/.well-known/", StringComparison.OrdinalIgnoreCase);
        if (publicHost ? !OpenApiPolicy.IsPublicPath(path) : protocolPath || !settings.Enabled && path.StartsWith("/api/open-api", StringComparison.OrdinalIgnoreCase)) { context.Response.StatusCode = 404; return; }
        if (!publicHost) { await next(); return; }
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Correlation-ID"] = context.TraceIdentifier;
        if (context.Request.ContentLength > 16384) { context.Response.StatusCode = 413; return; }
        var bodyLimit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodyLimit is { IsReadOnly: false }) bodyLimit.MaxRequestBodySize = 16384;
        var started = System.Diagnostics.Stopwatch.StartNew();
        try { await next(); }
        finally
        {
            // Paths and outcomes only: never log queries, authorization headers or bodies.
            if (path != "/health" && path != "/mini-app-sdk/ankt-mini-app.js")
            {
                try
                {
                // A separate context cannot accidentally flush failed business changes into the audit write.
                await using var auditScope = app.Services.CreateAsyncScope();
                var db = auditScope.ServiceProvider.GetRequiredService<AppDbContext>();
                Guid? appId = Guid.TryParse(context.User.GetClaim("app_id"), out var id) ? id : null;
                var clientId = context.GetOpenIddictServerRequest()?.ClientId;
                if (appId is null && !string.IsNullOrEmpty(clientId)) appId = await db.MiniApps.Where(a => a.ClientId == clientId).Select(a => (Guid?)a.Id).SingleOrDefaultAsync();
                db.MiniAppAuditLogs.Add(new() { MiniAppId = appId, Action = "OpenApiRequest", Detail = JsonSerializer.Serialize(new { path, method = context.Request.Method, status = context.Response.StatusCode, durationMs = started.ElapsedMilliseconds, correlationId = context.TraceIdentifier }) });
                await db.SaveChangesAsync();
                }
                catch { app.Logger.LogError("Open API audit persistence failed for {CorrelationId}", context.TraceIdentifier); }
            }
        }
    });
}
