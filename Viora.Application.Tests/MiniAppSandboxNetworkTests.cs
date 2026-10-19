using System.Net;
using Viora.Application.Developers;
using Viora.Infrastructure.Developers;
using Xunit;

public sealed class MiniAppSandboxNetworkTests
{
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
