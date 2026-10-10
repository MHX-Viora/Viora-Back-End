using System.Text.Json;
using Viora.Application.MiniApps;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.MiniApps;

internal static class MiniAppConfigurationSnapshot
{
    public static MiniAppConfigurationInput Draft(MiniApp app) => new(app.Name, app.Slug, app.Description, app.IconUrl, app.CoverUrl,
        app.WebUrl, app.CallbackUrl, app.AllowedDomains, app.PermissionMappings.Select(m => m.Permission.Code).Order().ToArray(),
        app.IsFeatured, app.CategoryId, app.AuthenticationMode.ToString(), app.CallbackUrls.Length > 0 ? app.CallbackUrls :
            string.IsNullOrEmpty(app.CallbackUrl) ? [] : [app.CallbackUrl],
        app.AllowedOrigins.Length > 0 ? app.AllowedOrigins : [new Uri(app.WebUrl).GetLeftPart(UriPartial.Authority)],
        app.ClientAuthenticationMethod.ToString());

    public static MiniAppConfigurationInput Published(MiniApp app) => app.PublishedConfigurationJson is null ? Draft(app) :
        JsonSerializer.Deserialize<MiniAppConfigurationInput>(app.PublishedConfigurationJson)!;
    public static string Serialize(MiniApp app) => JsonSerializer.Serialize(Draft(app));
    public static MiniAppVersionDto Version(MiniAppVersion v) => new(v.Id, v.Version, v.Status.ToString(), v.Reason,
        v.CreatedAt, v.ReviewedAt, JsonSerializer.Deserialize<MiniAppConfigurationInput>(v.ConfigurationJson)!);
}
