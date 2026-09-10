using Xunit;

namespace Viora.Application.Tests.Calls;

public sealed class WebCorsConfigurationTests
{
    [Fact]
    public void WebCorsUsesDeploymentExactOrigins()
    {
        var programPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../viora-BE/Program.cs"));
        var source = File.ReadAllText(programPath);

        Assert.Contains("GetSection(\"Cors:AllowedOrigins\")", source);
        Assert.Contains("WithOrigins(webOrigins)", source);
        Assert.Contains("builder.Environment.IsDevelopment()", source);
        Assert.Contains("Cors:AllowedOrigins must be configured outside Development", source);
        Assert.DoesNotContain("AllowAnyOrigin", source);
    }
}
