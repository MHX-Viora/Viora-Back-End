using System.Net;
using Viora.Infrastructure.MiniApps;
using Xunit;

public sealed class DeveloperDomainProbeTests
{
    private sealed class ResponseHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("https", request.RequestUri!.Scheme);
            Assert.Null(request.Headers.Authorization);
            Assert.DoesNotContain(".well-known", request.RequestUri.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
    [Theory]
    [InlineData(404, "", "DOMAIN_FILE_NOT_FOUND")]
    [InlineData(302, "", "DOMAIN_REDIRECT_REJECTED")]
    [InlineData(200, "wrong-token", "DOMAIN_CONTENT_MISMATCH")]
    public async Task ReportsActionableErrors(int status, string body, string code)
    {
        var verifier = new MiniAppDomainVerifier(() => new ResponseHandler((HttpStatusCode)status, body));
        var result = await verifier.CheckAsync("example.com", "ankt-verify-" + new string('a', 32) + ".txt", "ankt-domain-verification=fixture", default);
        Assert.False(result.Verified); Assert.Equal(code, result.Code);
    }
    [Fact]
    public async Task MatchesOnlyExpectedTextAndRejectsOversizedAndUnsafePaths()
    {
        var verifier = new MiniAppDomainVerifier(() => new ResponseHandler(HttpStatusCode.OK, "ankt-domain-verification=fixture"));
        Assert.True((await verifier.CheckAsync("example.com", "ankt-verify-" + new string('a', 32) + ".txt", "ankt-domain-verification=fixture", default)).Verified);
        Assert.Equal("DOMAIN_INVALID", (await verifier.CheckAsync("127.0.0.1", "ankt-mini-app-verification.txt", "fixture", default)).Code);
        Assert.Equal("DOMAIN_INVALID", (await verifier.CheckAsync("example.com", "../secrets.txt", "fixture", default)).Code);
        var large = new MiniAppDomainVerifier(() => new ResponseHandler(HttpStatusCode.OK, new string('x', 1025)));
        Assert.Equal("DOMAIN_FILE_TOO_LARGE", (await large.CheckAsync("example.com", "ankt-mini-app-verification.txt", "fixture", default)).Code);
    }
}
