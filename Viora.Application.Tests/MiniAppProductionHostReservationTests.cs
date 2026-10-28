using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Viora.Infrastructure.Developers;
using viora_BE.Production;
using Xunit;

public sealed class MiniAppProductionHostReservationTests
{
    [Theory]
    [InlineData("MiniAppAuth:AllowedHostOrigin")]
    [InlineData("MiniAppSandbox:HostOrigin")]
    public void ActualConfiguredPlatformHostnameCannotBecomeProductionHostEvenIfOptionalDuplicateSettingIsMissing(string key)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MiniAppRuntime:Enabled"] = "true", ["MiniAppRuntime:HostOrigin"] = "https://host.example.com",
            ["MiniAppRuntime:FirstPartyOrigin"] = "https://app.example.com", ["MiniAppRuntime:ApiOrigin"] = "https://api.example.com",
            [key] = "https://host.example.com/"
        }).Build();
        var services = new ServiceCollection(); services.Configure<MiniAppReviewOptions>(o => o.Enabled = true);
        services.AddMiniAppProductionServices(configuration);
        using var provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<MiniAppProductionOptions>>().Value);
    }
}
