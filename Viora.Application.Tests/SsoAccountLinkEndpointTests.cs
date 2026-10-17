using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Viora.Application.Sso;
using Viora.Infrastructure.Sso;
using Xunit;

public sealed class SsoAccountLinkEndpointTests
{
    [Fact]
    public async Task WrappedPostgresSerializationConflictReturns409AndDoesNotConsumeProof()
    {
        var failure=new WrappedConflict();await using var host=await SsoProtocolTestHost.Create(configureDatabase:o=>o.AddInterceptors(failure));
        const string secret="isolated-wrapped-conflict-secret";await host.MakeConfidential(secret);
        var grant=await host.WithServices(s=>s.GetRequiredService<SsoConsentService>().GrantAsync(host.AccountId,SsoProtocolTestHost.ClientId,["openid"],default));
        var proof=await host.WithServices(s=>s.GetRequiredService<SsoAccountLinkService>().IssueAsync(SsoProtocolTestHost.ClientId,secret,grant.Subject,"partner-owned",default));
        host.Client.DefaultRequestHeaders.Authorization=new("Bearer",host.SocialAccessToken());failure.Armed=true;
        var response=await host.Client.PostAsJsonAsync("/api/sso/account-links/confirm",new {proof.Challenge,approve=true});
        Assert.Equal(HttpStatusCode.Conflict,response.StatusCode);Assert.Contains("SSO_LINK_CONFLICT",await response.Content.ReadAsStringAsync());
        failure.Armed=false;
        Assert.Equal(HttpStatusCode.OK,(await host.Client.PostAsJsonAsync("/api/sso/account-links/confirm",new {proof.Challenge,approve=true})).StatusCode);
    }
    private sealed class WrappedConflict:Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
    {
        public bool Armed;
        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> NonQueryExecutingAsync(
            System.Data.Common.DbCommand command,Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData data,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,CancellationToken cancellationToken=default)
        {
            if(Armed&&command.CommandText.Contains("UPDATE \"SsoAccountLinkChallenges\"",StringComparison.Ordinal))
                throw new InvalidOperationException("Isolated wrapper",new Npgsql.PostgresException("Isolated serialization failure","ERROR","ERROR","40001"));
            return ValueTask.FromResult(result);
        }
    }
    [Fact]
    public async Task PartnerAttestationRequiresBackendSecretAndOwnerMustExplicitlyApproveWithLiveSocialSession()
    {
        await using var host = await SsoProtocolTestHost.Create();
        const string secret = "isolated-http-account-link-secret"; await host.MakeConfidential(secret);
        var grant = await host.WithServices(s => s.GetRequiredService<SsoConsentService>().GrantAsync(host.AccountId, SsoProtocolTestHost.ClientId, ["openid"], default));
        var body = new { subject = grant.Subject, partnerAccountId = "partner-server-session-id" };
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.PostAsJsonAsync("/api/sso/account-links/challenges", body)).StatusCode);
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(SsoProtocolTestHost.ClientId + ":" + secret)));
        host.Client.DefaultRequestHeaders.Add("Origin", "https://partner.example.com");
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.PostAsJsonAsync("/api/sso/account-links/challenges", body)).StatusCode);
        host.Client.DefaultRequestHeaders.Remove("Origin");
        var response = await host.Client.PostAsJsonAsync("/api/sso/account-links/challenges", body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl!.NoStore);
        var proof = (await response.Content.ReadFromJsonAsync<SsoAccountLinkProof>())!;
        host.Client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.PostAsJsonAsync("/api/sso/account-links/confirm", new { proof.Challenge, approve = true })).StatusCode);
        host.Client.DefaultRequestHeaders.Authorization = new("Bearer", host.SocialAccessToken(tokenType: "developer_access"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.PostAsJsonAsync("/api/sso/account-links/confirm", new { proof.Challenge, approve = true })).StatusCode);
        host.Client.DefaultRequestHeaders.Authorization = new("Bearer", host.SocialAccessToken());
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.PostAsJsonAsync("/api/sso/account-links/confirm", new { proof.Challenge })).StatusCode);
        var preview = await host.Client.PostAsJsonAsync("/api/sso/account-links/preview", new { proof.Challenge });
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var confirmed = await host.Client.PostAsJsonAsync("/api/sso/account-links/confirm", new { proof.Challenge, approve = true });
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.PostAsJsonAsync("/api/sso/account-links/confirm", new { proof.Challenge, approve = true })).StatusCode);
        var link = (await confirmed.Content.ReadFromJsonAsync<SsoAccountLinkDto>())!;
        Assert.Equal(HttpStatusCode.NoContent, (await host.Client.DeleteAsync("/api/sso/account-links/mine/" + link.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await host.Client.DeleteAsync("/api/sso/account-links/mine/" + link.Id)).StatusCode);
    }
}
