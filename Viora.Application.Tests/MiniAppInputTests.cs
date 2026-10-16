using Viora.Application.Developers;
using Xunit;

public sealed class MiniAppInputTests
{
    [Theory]
    [InlineData("https://example.com/callback", false, true)]
    [InlineData("http://localhost:8081/callback", true, true)]
    [InlineData("http://localhost:8081/callback", false, false)]
    [InlineData("https://example.com/cb#token", false, false)]
    [InlineData("https://*.example.com/cb", false, false)]
    [InlineData("https://user:pass@example.com", false, false)]
    [InlineData("javascript:alert(1)", false, false)]
    [InlineData("http://example.com", true, false)]
    public void CallbackPolicy(string url, bool development, bool valid)
    {
        if (valid) Assert.Equal(url, MiniAppInput.Url(url, development, true));
        else Assert.Throws<DeveloperAuthException>(() => MiniAppInput.Url(url, development, true));
    }

    [Theory]
    [InlineData("", "utilities")]
    [InlineData("Valid", "unknown")]
    public void InvalidCreationRejected(string name, string category) =>
        Assert.Throws<DeveloperAuthException>(() => MiniAppInput.ValidateCreate(new(name, category, Guid.NewGuid())));
}
