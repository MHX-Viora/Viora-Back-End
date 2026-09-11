using Viora.Application.Configuration;
using Xunit;

namespace Viora.Application.Tests.Configuration;

public sealed class WebCorsOriginsTests
{
    [Fact]
    public void Resolve_uses_development_defaults_when_not_configured()
    {
        var origins = WebCorsOrigins.Resolve([]);

        Assert.Contains("http://localhost:5173", origins);
        Assert.Contains("https://vioraadmin.vercel.app", origins);
    }

    [Fact]
    public void Resolve_normalizes_configured_origins_without_development_defaults()
    {
        var origins = WebCorsOrigins.Resolve([
            " https://ankt.example/ ",
            "https://ankt.example",
            "not-an-origin"
        ]);

        Assert.DoesNotContain("http://localhost:5173", origins);
        Assert.DoesNotContain("https://vioraadmin.vercel.app", origins);
        Assert.Equal(1, origins.Count(origin => origin == "https://ankt.example"));
        Assert.DoesNotContain("not-an-origin", origins);
    }
}
