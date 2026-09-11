using Viora.Application.MiniApps;
using Xunit;

namespace Viora.Application.Tests;

public sealed class MiniAppSecurityPolicyTests
{
    [Theory]
    [InlineData("https://antifake.vn/ankt", "antifake.vn", true)]
    [InlineData("https://login.antifake.vn/ankt", "*.antifake.vn", true)]
    [InlineData("https://antifake.vn.evil.test/ankt", "*.antifake.vn", false)]
    [InlineData("http://antifake.vn/ankt", "antifake.vn", false)]
    [InlineData("javascript:alert(1)", "antifake.vn", false)]
    public void IsAllowedHttpsUrl_enforces_scheme_and_host_boundary(string url, string allowedDomain, bool expected)
    {
        Assert.Equal(expected, MiniAppSecurityPolicy.IsAllowedHttpsUrl(url, [allowedDomain]));
    }

    [Fact]
    public void BuildLaunchUrl_replaces_existing_code_and_preserves_other_query_values()
    {
        var url = MiniAppSecurityPolicy.BuildLaunchUrl(
            "https://partner.test/ankt/launch?campaign=summer&code=old",
            "LCH_safe-code");

        Assert.Equal("https://partner.test/ankt/launch?campaign=summer&code=LCH_safe-code", url);
    }

    [Fact]
    public void ProjectProfile_returns_only_fields_authorized_by_granted_scopes()
    {
        var profile = MiniAppSecurityPolicy.ProjectProfile(
            "subject-1", "An", "https://cdn.test/a.png", "an@test.vn", "+8490",
            ["identity.login", "profile.basic"]);

        Assert.Equal("subject-1", profile.Subject);
        Assert.Equal("An", profile.DisplayName);
        Assert.Null(profile.Email);
        Assert.Null(profile.Phone);
    }

    [Fact]
    public void ProjectProfile_does_not_return_subject_without_identity_login_scope()
    {
        var profile = MiniAppSecurityPolicy.ProjectProfile(
            "subject-1", "An", null, null, null, ["profile.basic"]);

        Assert.Null(profile.Subject);
        Assert.Equal("An", profile.DisplayName);
    }
}
