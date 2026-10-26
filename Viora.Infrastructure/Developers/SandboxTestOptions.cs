using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Viora.Infrastructure.Developers;

public sealed class SandboxTestOptions
{
    public bool Enabled { get; set; }
    public string AvatarUrl { get; set; } = "https://res.cloudinary.com/xxgrbmfg/image/upload/v1792829497/ankt/sandbox/avatar-v1.svg";
    public string CoverUrl { get; set; } = "https://res.cloudinary.com/xxgrbmfg/image/upload/v1792829499/ankt/sandbox/cover-v1.svg";
    public static bool ValidPortalOrigin(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == "https" || uri.Scheme == "http" && uri.IsLoopback) &&
        uri.UserInfo.Length == 0 && value == uri.GetLeftPart(UriPartial.Authority);
    public static bool CloudinaryUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == "https" && uri.Host == "res.cloudinary.com" && uri.UserInfo.Length == 0 &&
        uri.AbsolutePath.Contains("/image/upload/", StringComparison.Ordinal) && uri.Query.Length == 0 && uri.Fragment.Length == 0;
}

public static class SandboxTestRegistration
{
    public static IServiceCollection AddSandboxTestIdentity(this IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(x => x.ServiceType == typeof(SandboxTestService))) return services;
        services.AddOptions<SandboxTestOptions>().Bind(configuration.GetSection("SandboxTest"))
            .Validate(x => !x.Enabled || SandboxTestOptions.CloudinaryUrl(x.AvatarUrl) && SandboxTestOptions.CloudinaryUrl(x.CoverUrl),
                "Enabled Sandbox requires valid Cloudinary profile asset URLs.");
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<SandboxTestService>();
        return services;
    }
}
