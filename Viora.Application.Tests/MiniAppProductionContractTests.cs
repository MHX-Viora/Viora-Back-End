using System.Text.Json;
using Viora.Application.Developers;
using Viora.Infrastructure.Developers;
using Xunit;

public sealed class MiniAppProductionContractTests
{
    [Theory]
    [InlineData(0, 20)]
    [InlineData(100001, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public void CatalogRejectsUnboundedOrInvalidPagination(int page, int size)
    {
        var error = Assert.Throws<DeveloperAuthException>(() => new MiniAppPublicCatalogQuery(page, size).Validate());
        Assert.Equal(400, error.Status);
    }

    [Fact]
    public void PublicDisplayContractDoesNotSerializeRuntimeOrPrivateIdentity()
    {
        var page = new MiniAppPublicPageDto([new("app_" + new string('a', 43), "Published name",
            "https://cdn.example.com/icon.png", "Public description", "services", "1.0")], 1, 20, 1);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(page, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(["id", "name", "logoUrl", "description", "category", "version"],
            json.RootElement.GetProperty("items")[0].EnumerateObject().Select(x => x.Name));
        Assert.Equal(1, json.RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public void ProductionRuntimeDefaultsOffAndCannotEnableWithoutReviewAuthority()
    {
        Assert.False(new MiniAppProductionOptions().Enabled);
        Assert.False(new MiniAppProductionOptions().NativeEnabled);
        var validator = new MiniAppProductionOptionsValidator(new MiniAppReviewOptions());
        Assert.True(validator.Validate(null, new()).Succeeded);
        Assert.True(validator.Validate(null, Valid()).Failed);
        validator = new(new MiniAppReviewOptions { Enabled = true });
        Assert.True(validator.Validate(null, Valid()).Succeeded);
    }

    [Theory]
    [InlineData("http://host.example.com")]
    [InlineData("https://name:password@host.example.com")]
    [InlineData("https://host.example.com/path")]
    [InlineData("https://host.example.com?token=x")]
    [InlineData("https://app.example.com:8443")]
    [InlineData("https://api.example.com")]
    [InlineData("https://identity.example.com")]
    [InlineData("https://sandbox.example.com")]
    public void HostMustBeASeparateHttpsCookieHost(string host)
    {
        var options = Valid(); options.HostOrigin = host;
        Assert.True(new MiniAppProductionOptionsValidator(new() { Enabled = true }).Validate(null, options).Failed);
    }

    [Theory]
    [InlineData(0, 30, 15)]
    [InlineData(31, 30, 15)]
    [InlineData(15, 31, 15)]
    [InlineData(15, 0, 15)]
    [InlineData(15, 30, 0)]
    public void UnboundedSessionOrGrantPolicyCannotStart(int session, int grant, int heartbeat)
    {
        var options = Valid(); options.SessionMinutes = session; options.GrantSeconds = grant; options.HeartbeatSeconds = heartbeat;
        Assert.True(new MiniAppProductionOptionsValidator(new() { Enabled = true }).Validate(null, options).Failed);
    }

    private static MiniAppProductionOptions Valid() => new()
    {
        Enabled = true, FirstPartyOrigin = "https://app.example.com", HostOrigin = "https://host.example.com",
        ApiOrigin = "https://api.example.com", IssuerOrigin = "https://identity.example.com", SandboxHostOrigin = "https://sandbox.example.com"
    };
}
