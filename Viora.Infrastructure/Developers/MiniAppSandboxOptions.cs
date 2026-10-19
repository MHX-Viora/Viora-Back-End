using Microsoft.Extensions.Options;

namespace Viora.Infrastructure.Developers;

public sealed class MiniAppSandboxOptions
{
    public const string SectionName = "MiniAppSandbox";
    public bool Enabled { get; set; }
    public string PortalOrigin { get; set; } = "";
    public string HostOrigin { get; set; } = "";
    public string Issuer { get; set; } = "";
    public int SessionMinutes { get; set; } = 15;
    public int GrantSeconds { get; set; } = 30;
    public int ActiveSessionsPerActor { get; set; } = 3;
    public Guid[] AdminTesterIds { get; set; } = [];
}

public sealed class MiniAppSandboxOptionsValidator : IValidateOptions<MiniAppSandboxOptions>
{
    public ValidateOptionsResult Validate(string? name, MiniAppSandboxOptions options)
    {
        if (!options.Enabled) return ValidateOptionsResult.Success;
        static bool Origin(string value, out Uri? uri) => Uri.TryCreate(value, UriKind.Absolute, out uri) &&
            uri.Scheme == "https" && uri.UserInfo.Length == 0 && uri.AbsolutePath == "/" && uri.Query.Length == 0 && uri.Fragment.Length == 0;
        if (!Origin(options.PortalOrigin, out var portal) || !Origin(options.HostOrigin, out var host) ||
            !Uri.TryCreate(options.Issuer, UriKind.Absolute, out var issuer) || issuer.Scheme != "https" ||
            issuer.UserInfo.Length != 0 || issuer.Query.Length != 0 || issuer.Fragment.Length != 0 ||
            host!.Host == portal!.Host || host.Host == issuer.Host)
            return ValidateOptionsResult.Fail("Sandbox requires distinct HTTPS host, portal and issuer authorities; its hostname must isolate cookies.");
        if (options.SessionMinutes is < 1 or > 30 || options.GrantSeconds is < 1 or > 30 || options.ActiveSessionsPerActor is < 1 or > 3 ||
            options.AdminTesterIds.Length > 100 || options.AdminTesterIds.Any(x => x == Guid.Empty))
            return ValidateOptionsResult.Fail("Sandbox lifetime, session quota or tester configuration is invalid.");
        return ValidateOptionsResult.Success;
    }
}
