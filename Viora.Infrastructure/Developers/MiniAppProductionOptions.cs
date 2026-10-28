using Microsoft.Extensions.Options;

namespace Viora.Infrastructure.Developers;

public sealed class MiniAppProductionOptions
{
    private readonly HashSet<string> reservedHosts = new(StringComparer.OrdinalIgnoreCase);
    public void ReserveConfiguredHost(string? value)
    {
        var host = Hostname(value); if (host != null) reservedHosts.Add(host);
    }
    public bool IsReservedHost(string hostname) => reservedHosts.Contains(hostname.TrimEnd('.')) ||
        new[] { FirstPartyOrigin, ApiOrigin, IssuerOrigin, SandboxHostOrigin }.Any(value =>
            string.Equals(Hostname(value), hostname.TrimEnd('.'), StringComparison.OrdinalIgnoreCase));
    private static string? Hostname(string? value)
    {
        try { return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.IdnHost.Length > 0 ? uri.IdnHost.TrimEnd('.') : null; }
        catch (UriFormatException) { return null; }
    }
    public const string SectionName = "MiniAppRuntime";
    public bool Enabled { get; set; }
    public bool CleanupEnabled { get; set; }
    public bool NativeEnabled { get; set; }
    public string FirstPartyOrigin { get; set; } = "";
    public string HostOrigin { get; set; } = "";
    public string ApiOrigin { get; set; } = "";
    public string IssuerOrigin { get; set; } = "";
    public string SandboxHostOrigin { get; set; } = "";
    public int SessionMinutes { get; set; } = 15;
    public int GrantSeconds { get; set; } = 30;
    public int HeartbeatSeconds { get; set; } = 15;
    public int ActiveSessionsPerBinding { get; set; } = 3;
    public int EventRetentionDays { get; set; } = 7;
    public int ReadLimitPerMinute { get; set; } = 120;
    public int PrepareLimitPerMinute { get; set; } = 30;
    public int LaunchLimitPerMinute { get; set; } = 20;
    public int RedeemLimitPerMinute { get; set; } = 30;
    public int RuntimeLimitPerMinute { get; set; } = 120;
    public int EventsPerSessionPerMinute { get; set; } = 10;
}

public sealed class MiniAppProductionOptionsValidator(MiniAppReviewOptions review) : IValidateOptions<MiniAppProductionOptions>
{
    public ValidateOptionsResult Validate(string? name, MiniAppProductionOptions options)
    {
        if ((options.Enabled || options.CleanupEnabled) && options.EventRetentionDays is < 1 or > 30)
            return ValidateOptionsResult.Fail("Production runtime retention is outside policy limits.");
        if (!options.Enabled) return ValidateOptionsResult.Success;
        if (!review.Enabled) return ValidateOptionsResult.Fail("Production runtime requires Stage 7 review/publication authority.");
        static bool Origin(string value, out Uri? uri) => Uri.TryCreate(value, UriKind.Absolute, out uri) &&
            uri.Scheme == "https" && uri.UserInfo.Length == 0 && value == uri.GetLeftPart(UriPartial.Authority);
        if (!Origin(options.HostOrigin, out var host) || !Origin(options.FirstPartyOrigin, out var firstParty) ||
            !Origin(options.ApiOrigin, out var api))
            return ValidateOptionsResult.Fail("Production host, first-party app and API require exact HTTPS origins.");
        var reservedHosts = new List<string> { firstParty!.Host, api!.Host };
        foreach (var value in new[] { options.IssuerOrigin, options.SandboxHostOrigin })
        {
            if (value.Length == 0) continue;
            if (!Origin(value, out var uri)) return ValidateOptionsResult.Fail("Reserved platform origins must be exact HTTPS origins.");
            reservedHosts.Add(uri!.Host);
        }
        if (reservedHosts.Contains(host!.Host, StringComparer.OrdinalIgnoreCase) || options.IsReservedHost(host.IdnHost))
            return ValidateOptionsResult.Fail("Production host requires a separate hostname for cookie and document isolation.");
        if (options.SessionMinutes is < 1 or > 30 || options.GrantSeconds is < 1 or > 30 ||
            options.HeartbeatSeconds is < 5 or > 60 || options.ActiveSessionsPerBinding is < 1 or > 3 ||
            options.EventRetentionDays is < 1 or > 30 || options.ReadLimitPerMinute is < 1 or > 1000 ||
            options.PrepareLimitPerMinute is < 1 or > 30 || options.LaunchLimitPerMinute is < 1 or > 20 ||
            options.RedeemLimitPerMinute is < 1 or > 30 || options.RuntimeLimitPerMinute is < 1 or > 120 ||
            options.EventsPerSessionPerMinute is < 1 or > 10)
            return ValidateOptionsResult.Fail("Production runtime lifetime, quota or retention is outside policy limits.");
        return ValidateOptionsResult.Success;
    }
}
