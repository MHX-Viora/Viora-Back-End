using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Viora.Application.MiniApps;
using Viora.Domain.Entities;
using Viora.Infrastructure.OpenApi;
using Viora.Infrastructure.Persistence;
using viora_BE.OpenApi;

namespace viora_BE.Controllers.OpenApi;

public sealed record OpenApiUsage(int Requests, int Successful, int Unauthorized, int Forbidden, int RateLimited, bool Truncated);
public sealed record OpenApiRecentError(DateTime OccurredAt, string Path, int Status, string? CorrelationId);
public sealed record OpenApiSsoReadiness(bool AutoLoginEnabled, bool OAuthConfigurationValid, bool CallbackDomainsVerified, bool PkceSupported, string SilentSsoStatus, string ConsentRequirements, string AccountLinkingStatus);
public sealed record OpenApiManagementView(bool Enabled, string? Issuer, string? DiscoveryUrl, string? HostOrigin, string ClientId,
    string[] ApprovedScopes, IReadOnlyList<string> ApprovedCallbackUrls, bool Eligible, int AccountLinks, OpenApiUsage Usage, IReadOnlyList<OpenApiRecentError>? RecentErrors = null, OpenApiSsoReadiness? SsoReadiness = null);

internal static class OpenApiManagementViews
{
    public static async Task<OpenApiManagementView> Build(AppDbContext db, PartnerOpenApiOptions settings, MiniApp app, CancellationToken ct)
    {
        var published = app.PublishedConfigurationJson is null ? null : JsonSerializer.Deserialize<MiniAppConfigurationInput>(app.PublishedConfigurationJson);
        var hasPublished = published is not null || app.PublishedVersion > 0 || app.Status is MiniAppStatus.Active or MiniAppStatus.Suspended;
        var permissions = published?.Permissions ?? (hasPublished ? app.PermissionMappings.Select(m => m.Permission.Code).ToArray() : Array.Empty<string>());
        var activePermissions = (await db.MiniAppPermissions.AsNoTracking().Where(p => permissions.Contains(p.Code) && p.Status == MiniAppPermissionStatus.Active).Select(p => p.Code).ToArrayAsync(ct)).ToHashSet();
        var scopes = OpenApiPolicy.ScopePermissions.Where(s => permissions.Contains(s.Value) && activePermissions.Contains(s.Value)).Select(s => s.Key).ToArray();
        IReadOnlyList<string> callbacks = !hasPublished ? [] : published is not null ? published.CallbackUrls ?? (string.IsNullOrEmpty(published.CallbackUrl) ? [] : new[] { published.CallbackUrl })
            : app.CallbackUrls.Length > 0 ? app.CallbackUrls : string.IsNullOrEmpty(app.CallbackUrl) ? [] : new[] { app.CallbackUrl };
        var since = DateTime.UtcNow.AddDays(-7);
        var details = await db.MiniAppAuditLogs.AsNoTracking().Where(l => l.MiniAppId == app.Id && l.Action == "OpenApiRequest" && l.CreatedAt >= since)
            .OrderByDescending(l => l.CreatedAt).Take(10001).Select(l => new { l.Detail, l.CreatedAt }).ToListAsync(ct);
        var statuses = new List<int>();
        var errors = new List<OpenApiRecentError>();
        foreach (var record in details.Take(10000))
        {
            try
            {
                using var document = JsonDocument.Parse(record.Detail ?? "{}");
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.Number || !status.TryGetInt32(out var value) || value is < 100 or > 599) continue;
                statuses.Add(value);
                if (value < 400 || errors.Count >= 10 || !root.TryGetProperty("path", out var pathValue) || pathValue.ValueKind != JsonValueKind.String) continue;
                // Older records may contain URL queries. Return only an allowlisted path.
                var path = pathValue.GetString()?.Split(new[] { '?', '#' }, 2)[0];
                if (path is null || !OpenApiPolicy.IsPublicPath(path)) continue;
                string? correlation = null;
                if (root.TryGetProperty("correlationId", out var correlationValue) && correlationValue.ValueKind == JsonValueKind.String)
                {
                    var candidate = correlationValue.GetString();
                    if (candidate is not null && System.Text.RegularExpressions.Regex.IsMatch(candidate, "^[A-Za-z0-9:_-]{1,128}$")) correlation = candidate;
                }
                errors.Add(new(record.CreatedAt, path, value, correlation));
            }
            catch (JsonException) { /* Older audit records remain readable. */ }
        }
        var links = await db.MiniAppExternalIdentities.CountAsync(i => i.MiniAppId == app.Id, ct);
        var verifiedHosts = await db.MiniAppVerifiedDomains.Where(d => d.MiniAppId == app.Id && d.VerifiedAt != null).Select(d => d.Host).ToArrayAsync(ct);
        var callbackVerified = callbacks.Count > 0 && callbacks.All(c => Uri.TryCreate(c, UriKind.Absolute, out var uri) && uri.Scheme == "https" && verifiedHosts.Contains(uri.IdnHost));
        var autoLogin = hasPublished && (published?.AutoLogin ?? app.AutoLogin);
        return new(settings.Enabled, settings.Enabled ? settings.Issuer : null, settings.Enabled ? settings.Issuer.TrimEnd('/') + "/.well-known/openid-configuration" : null,
            settings.Enabled && Uri.TryCreate(settings.AuthorizationUi, UriKind.Absolute, out var ui) ? ui.GetLeftPart(UriPartial.Authority) : null,
            app.ClientId, scopes, callbacks, settings.Enabled && app.Status == MiniAppStatus.Active && app.Developer.Status == DeveloperStatus.Active && (published?.AuthenticationMode ?? app.AuthenticationMode.ToString()) == "AnktSso" && scopes.Contains("openid"), links,
            new(statuses.Count, statuses.Count(s => s is >= 200 and < 400), statuses.Count(s => s == 401), statuses.Count(s => s == 403), statuses.Count(s => s == 429), details.Count > 10000), errors,
            new(autoLogin, settings.Enabled && hasPublished && scopes.Contains("openid") && callbackVerified, callbackVerified, true,
                !autoLogin ? "Disabled" : "PartnerVerificationRequired", "ExplicitConsentForMissingScopes", "PairwiseSubjectSupportedPartnerVerificationRequired"));
    }
}
