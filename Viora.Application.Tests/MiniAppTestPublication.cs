using System.Text.Json;
using Viora.Application.MiniApps;
using Viora.Domain.Entities;

// Protocol fixtures represent an already reviewed and published application.
internal static class MiniAppTestPublication
{
    public static void Publish(MiniApp app, params string[] permissions)
    {
        app.PublishedVersion = 1;
        app.PublishedConfigurationJson = JsonSerializer.Serialize(new MiniAppConfigurationInput(
            app.Name, app.Slug, app.Description, app.IconUrl, app.CoverUrl, app.WebUrl, app.CallbackUrl,
            app.AllowedDomains, permissions, AuthenticationMode: app.AuthenticationMode.ToString(),
            CallbackUrls: app.CallbackUrls, AllowedOrigins: app.AllowedOrigins,
            ClientAuthenticationMethod: app.ClientAuthenticationMethod.ToString()));
    }
}
