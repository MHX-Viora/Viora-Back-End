using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Viora.Infrastructure.Developers;

public sealed class SandboxTestOptions
{
    public bool Enabled { get; set; }
    public bool SsoEnabled { get; set; }
    public string Issuer { get; set; } = "";
    public string PortalOrigin { get; set; } = "https://mxh.ankt.vn";
    public string[] AdditionalPortalOrigins { get; set; } = [];
    public string AvatarUrl { get; set; } = "https://res.cloudinary.com/xxgrbmfg/image/upload/v1792829497/ankt/sandbox/avatar-v1.svg";
    public string CoverUrl { get; set; } = "https://res.cloudinary.com/xxgrbmfg/image/upload/v1792829499/ankt/sandbox/cover-v1.svg";
    public int SessionMinutes { get; set; } = 15;
    public static bool ValidPortalOrigin(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == "https" || uri.Scheme == "http" && uri.IsLoopback) &&
        uri.UserInfo.Length == 0 && value == uri.GetLeftPart(UriPartial.Authority);
    public bool AllowsPortal(string value) => value == PortalOrigin || AdditionalPortalOrigins.Contains(value, StringComparer.Ordinal);
    public static bool CloudinaryUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == "https" && uri.Host == "res.cloudinary.com" && uri.UserInfo.Length == 0 &&
        uri.AbsolutePath.Contains("/image/upload/", StringComparison.Ordinal) && uri.Query.Length == 0 && uri.Fragment.Length == 0;
}

public static class SandboxTestRegistration
{
    public static IServiceCollection AddSandboxTestIdentity(this IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(x => x.ServiceType == typeof(SandboxTestService))) return services;
        services.AddOptions<SandboxTestOptions>().Bind(configuration.GetSection("SandboxTest")).PostConfigure<IHostEnvironment>((options, environment) =>
        {
            if (environment.IsProduction()) options.Enabled = false;
            options.SsoEnabled = configuration.GetValue<bool>("Sso:Enabled");
            options.Issuer = configuration["Sso:Issuer"] ?? "";
            if (configuration["SandboxTest:PortalOrigin"] == null &&
                Uri.TryCreate(configuration["DeveloperAuth:PortalUrl"], UriKind.Absolute, out var portal))
                options.PortalOrigin = portal.GetLeftPart(UriPartial.Authority);
        }).Validate(x => x.SessionMinutes is >= 1 and <= 15 && SandboxTestOptions.ValidPortalOrigin(x.PortalOrigin) &&
            x.AdditionalPortalOrigins.Length <= 8 && x.AdditionalPortalOrigins.All(SandboxTestOptions.ValidPortalOrigin),
            "Sandbox test requires 1–15 minute sessions and exact HTTPS Portal origins (HTTP only for explicit loopback development).");
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<SandboxTestService>();
        return services;
    }
}
