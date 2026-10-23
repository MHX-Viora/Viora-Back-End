using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.Sso;

namespace viora_BE.Sso;

// Adds a named partner policy; all existing application policies retain their configuration.
public sealed class SsoCorsPolicyProvider(IOptions<CorsOptions> options) : ICorsPolicyProvider
{
    public const string PolicyName = "SsoPublicClient";
    public async Task<CorsPolicy?> GetPolicyAsync(HttpContext context, string? policyName)
    {
        var path = context.Request.Path;
        if (path == "/.well-known/openid-configuration" || path == "/.well-known/jwks.json")
            return new CorsPolicyBuilder().AllowAnyOrigin().WithMethods("GET").Build();
        if (policyName != PolicyName) return options.Value.GetPolicy(policyName ?? options.Value.DefaultPolicyName);
        var denied = new CorsPolicyBuilder().Build();
        if (context.Request.Headers.Origin.Count != 1) return denied;
        var origin = context.Request.Headers.Origin.ToString();
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != "https" || origin != uri.GetLeftPart(UriPartial.Authority)) return denied;
        var db = context.RequestServices.GetRequiredService<AppDbContext>();
        var callbacks = await (from redirect in db.MiniAppRedirectUris.AsNoTracking()
            join client in db.MiniAppOAuthClients.AsNoTracking() on new { redirect.MiniAppId, redirect.Environment } equals new { client.MiniAppId, client.Environment }
            where client.Environment == MiniAppOAuthEnvironment.Sandbox && client.ClientType == "Public" && client.RevokedAt == null
            select new { client.ClientKey, redirect.RedirectUri }).ToListAsync(context.RequestAborted);
        // Callback spelling is preserved for exact protocol binding; compare parsed origins here.
        var production = await (from publication in db.MiniAppPublications.AsNoTracking()
            join client in db.MiniAppOAuthClients.AsNoTracking() on publication.ProductionOAuthClientId equals (Guid?)client.Id
            where publication.Status == MiniAppPublicationStatus.Published && client.RevokedAt == null
            select client.ClientKey).ToListAsync(context.RequestAborted);
        var candidates = callbacks.Where(x => Uri.TryCreate(x.RedirectUri, UriKind.Absolute, out var callback) &&
            callback.GetLeftPart(UriPartial.Authority) == origin).Select(x => x.ClientKey).Distinct(StringComparer.Ordinal);
        var clients = context.RequestServices.GetRequiredService<SsoClientReader>();
        foreach (var key in candidates.Concat(production).Distinct(StringComparer.Ordinal))
        {
            try
            {
                var client = await clients.EligibleAsync(key, ["openid"], null, context.RequestAborted);
                await clients.EnsureBrowserOriginAsync(client, origin, context.RequestAborted);
                context.Response.Headers.Append("Vary", "Origin");
                return new CorsPolicyBuilder().WithOrigins(origin).WithMethods("GET", "POST").WithHeaders("Authorization", "Content-Type").Build();
            }
            catch (DeveloperAuthException) { }
        }
        return denied;
    }
}
