using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Sso;
using Xunit;

public sealed class SsoClientPolicyTests
{
    [Fact]
    public void SandboxNeedsExplicitEligibilityAndProductionNeedsPublication()
    {
        var (client, app, proof) = Fixture();
        Assert.Throws<DeveloperAuthException>(() => SsoClientPolicy.EnsureEligible(client, app, proof, ["openid"], "Sandbox", DateTime.UtcNow));
        client.SandboxEnabled = true;
        SsoClientPolicy.EnsureEligible(client, app, proof, ["openid"], "Sandbox", DateTime.UtcNow);
        Assert.Throws<DeveloperAuthException>(() => SsoClientPolicy.EnsureEligible(client, app, proof, ["openid"], "Production", DateTime.UtcNow));
        client.Environment = MiniAppOAuthEnvironment.Production;
        Assert.Throws<DeveloperAuthException>(() => SsoClientPolicy.EnsureEligible(client, app, proof, ["openid"], "Production", DateTime.UtcNow));
        app.Status = MiniAppStatus.Published;
        SsoClientPolicy.EnsureEligible(client, app, proof, ["openid"], "Production", DateTime.UtcNow);
    }

    [Theory]
    [InlineData("suspended")]
    [InlineData("disabled")]
    [InlineData("revoked")]
    [InlineData("deleted")]
    [InlineData("developer")]
    [InlineData("domain")]
    [InlineData("expired")]
    [InlineData("scope")]
    public void LifecycleAndScopeChangesFailClosed(string change)
    {
        var (client, app, proof) = Fixture(); client.SandboxEnabled = true;
        switch (change)
        {
            case "suspended": app.Status = MiniAppStatus.Suspended; break;
            case "disabled": app.SsoEnabled = false; break;
            case "revoked": client.RevokedAt = DateTime.UtcNow; break;
            case "deleted": app.DeletedAt = DateTime.UtcNow; break;
            case "developer": app.Developer.Status = DeveloperStatus.Suspended; break;
            case "domain": proof.Status = MiniAppDomainStatus.Pending; break;
            case "expired": proof.ExpiresAt = DateTime.UtcNow.AddSeconds(-1); break;
            case "scope": client.AllowedScopes = "openid"; break;
        }
        Assert.Throws<DeveloperAuthException>(() => SsoClientPolicy.EnsureEligible(client, app, proof, ["openid", "profile"], "Sandbox", DateTime.UtcNow));
    }

    [Theory]
    [InlineData("https://partner.example.com/callback", true)]
    [InlineData("https://partner.example.com/callback/", false)]
    [InlineData("https://partner.example.com.evil.test/callback", false)]
    public void RedirectsRequireExactClientEnvironmentBinding(string uri, bool valid)
    {
        var (client, _, _) = Fixture();
        var redirect = new MiniAppRedirectUri { MiniAppId = client.MiniAppId, Environment = MiniAppOAuthEnvironment.Sandbox, RedirectUri = "https://partner.example.com/callback" };
        Assert.Equal(valid, SsoClientPolicy.IsRedirectAllowed(client, [redirect], uri));
        redirect.Environment = MiniAppOAuthEnvironment.Production;
        Assert.False(SsoClientPolicy.IsRedirectAllowed(client, [redirect], uri));
    }

    private static (MiniAppOAuthClient, MiniApp, MiniAppDomainVerification) Fixture()
    {
        var app = new MiniApp { SsoEnabled = true, WebsiteUrl = "https://partner.example.com", Status = MiniAppStatus.Draft,
            Developer = new DeveloperAccount { Status = DeveloperStatus.Active } };
        return (new MiniAppOAuthClient { MiniAppId = app.Id }, app,
            new MiniAppDomainVerification { MiniAppId = app.Id, Hostname = "partner.example.com", Status = MiniAppDomainStatus.Verified, ExpiresAt = DateTime.UtcNow.AddDays(1) });
    }
}
