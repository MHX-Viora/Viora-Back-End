using System.Net;
using Viora.Application.Developers;
using Viora.Infrastructure.Developers;
using Xunit;

public sealed class MiniAppSandboxNetworkTests
{
    [Theory]
    [InlineData("http://partner.example.com/help")]
    [InlineData("https://partner.example.com:444/help")]
    [InlineData("https://other.example.com/help")]
    [InlineData("https://user:pass@partner.example.com/help")]
    [InlineData("https://partner.example.com/help?code=secret")]
    [InlineData("https://partner.example.com/help#token")]
    [InlineData("https://partner.example.com/%63allback")]
    [InlineData("https://partner.example.com/connect/authorize")]
    [InlineData("https://partner.example.com/oauth2/login")]
    [InlineData("https://partner.example.com/sign-in")]
    [InlineData("https://partner.example.com/%2563allback")]
    [InlineData("https://partner.example.com/help\\callback")]
    public async Task ExternalLinksRejectUnverifiedOriginsAndAuthDestinations(string value)
    {
        var gate=new MiniAppSandboxNetwork(new Resolver([IPAddress.Parse("8.8.8.8")]));
        await Assert.ThrowsAsync<DeveloperAuthException>(()=>gate.ValidateExternalAsync(value,"https://partner.example.com",default));
    }
    [Fact]
    public async Task ExternalLinksUseFreshPublicDnsAndOnlyReturnNormalizedVerifiedUrl()
    {
        var resolver=new Resolver([IPAddress.Parse("8.8.8.8")]);var gate=new MiniAppSandboxNetwork(resolver);
        Assert.Equal("https://partner.example.com/help",await gate.ValidateExternalAsync("https://partner.example.com/help","https://partner.example.com",default));
        Assert.Equal("https://partner.example.com/authors",await gate.ValidateExternalAsync("https://partner.example.com/authors","https://partner.example.com",default));
        await Assert.ThrowsAsync<DeveloperAuthException>(()=>gate.ValidateExternalAsync("https://partner.example.com/complete","https://partner.example.com",default,["https://partner.example.com/complete"]));
        resolver.Answers=[IPAddress.Loopback];
        await Assert.ThrowsAsync<DeveloperAuthException>(()=>gate.ValidateExternalAsync("https://partner.example.com/help","https://partner.example.com",default));
    }
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("::1")]
    [InlineData("::ffff:8.8.8.8")]
    public async Task RejectsAnyPrivateAnswerEvenAlongsidePublicAddress(string address)
    {
        var gate=new MiniAppSandboxNetwork(new Resolver([IPAddress.Parse("8.8.8.8"),IPAddress.Parse(address)]));
        var error=await Assert.ThrowsAsync<DeveloperAuthException>(()=>gate.RequirePublicAsync("https://partner.example.com/",default));
        Assert.Equal(403,error.Status);
    }
    [Fact]
    public async Task ResolvesFreshOnEachLaunchAndFailsClosedForEmptyOrFailedDns()
    {
        var resolver=new Resolver([IPAddress.Parse("8.8.8.8")]);var gate=new MiniAppSandboxNetwork(resolver);
        await gate.RequirePublicAsync("https://partner.example.com/",default);
        resolver.Answers=[];await Assert.ThrowsAsync<DeveloperAuthException>(()=>gate.RequirePublicAsync("https://partner.example.com/",default));
        resolver.Failure=true;await Assert.ThrowsAsync<DeveloperAuthException>(()=>gate.RequirePublicAsync("https://partner.example.com/",default));
        Assert.Equal(3,resolver.Calls);
    }
    private sealed class Resolver(IPAddress[] answers):IPublicDnsResolver
    {
        public IPAddress[] Answers=answers;public bool Failure;public int Calls;
        public Task<IPAddress[]> ResolveAsync(string hostname,CancellationToken ct)
        {Calls++;Assert.Equal("partner.example.com",hostname);return Failure?Task.FromException<IPAddress[]>(new Exception("Do not expose DNS details")):Task.FromResult(Answers);}
    }
}
