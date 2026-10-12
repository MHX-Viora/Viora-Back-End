using Viora.Infrastructure.OpenApi;
using Xunit;

public sealed class OpenApiBoundaryTests
{
    [Theory]
    [InlineData("/oauth/token", true)]
    [InlineData("/oauth/authorize", true)]
    [InlineData("/.well-known/openid-configuration", true)]
    [InlineData("/v1/me", true)]
    [InlineData("/mini-app-sdk/ankt-mini-app.d.ts", true)]
    [InlineData("/api/admin", false)]
    [InlineData("/api/wallet", false)]
    [InlineData("/api/messages", false)]
    [InlineData("/api/payments", false)]
    [InlineData("/swagger/v1/swagger.json", false)]
    [InlineData("/oauth/token/extra", false)]
    public void PublicHostHasAnExplicitRouteAllowlist(string path, bool allowed)
        => Assert.Equal(allowed, OpenApiPolicy.IsPublicPath(path));

    [Fact]
    public void ScopeMappingRejectsRolesAndUnknownScopes()
    {
        Assert.Equal(new[] { "identity.login", "profile.email" }, OpenApiPolicy.PermissionsFor(new[] { "openid", "email" }));
        Assert.Throws<ArgumentException>(() => OpenApiPolicy.PermissionsFor(new[] { "admin" }));
        Assert.Throws<ArgumentException>(() => OpenApiPolicy.PermissionsFor(new[] { "offline_access" }));
    }

    [Fact]
    public void TransactionRequiresS256AndExactCallback()
    {
        Assert.True(OpenApiPolicy.ValidTransaction("https://partner.example/callback", new[] { "https://partner.example/callback" }, new string('s', 32), new string('c', 43), "S256", new string('n', 32)));
        Assert.False(OpenApiPolicy.ValidTransaction("https://partner.example/callback/", new[] { "https://partner.example/callback" }, new string('s', 32), new string('c', 43), "S256", new string('n', 32)));
        Assert.False(OpenApiPolicy.ValidTransaction("https://partner.example/callback", new[] { "https://partner.example/callback" }, "short", new string('c', 43), "plain", "short"));
        Assert.True(OpenApiPolicy.ValidTransaction("https://partner.example/callback", new[] { "https://partner.example/callback" }, new string('s', 1200), new string('c', 43), "S256", "638965863321.abcdefghijklmnop+/="));
        Assert.False(OpenApiPolicy.ValidTransaction("https://partner.example/callback", new[] { "https://partner.example/callback" }, new string('s', 4097), new string('c', 43), "S256", new string('n', 32)));
    }
}
