using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class SsoConsentEndpointTests
{
    [Theory]
    [InlineData("40001")]
    [InlineData("40P01")]
    public async Task CommitConflictsReturnRetryableConflictWithoutPersistingConsent(string sqlState)
    {
        var interceptor = new CommitConflict(sqlState);
        await using var host = await SsoProtocolTestHost.Create(configureDatabase: options => options.AddInterceptors(interceptor));
        var form = await Form(host); interceptor.Enabled = true;
        var response = await host.Client.PostAsync("/connect/authorize", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("SSO_CONSENT_CONFLICT", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, await host.WithServices(s => s.GetRequiredService<AppDbContext>().MiniAppUserConsents.CountAsync()));
    }

    private sealed class CommitConflict(string sqlState) : Microsoft.EntityFrameworkCore.Diagnostics.DbTransactionInterceptor
    {
        public bool Enabled;
        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult> TransactionCommittingAsync(
            System.Data.Common.DbTransaction transaction, Microsoft.EntityFrameworkCore.Diagnostics.TransactionEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Enabled) throw new Npgsql.PostgresException("simulated conflict", "ERROR", "ERROR", sqlState);
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task ExplicitConsentIssuesRealCodeAndExistingGrantSkipsConsent()
    {
        await using var host = await SsoProtocolTestHost.Create();
        var form = await Form(host);
        var accepted = await host.Client.PostAsync("/connect/authorize", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, accepted.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(QueryHelpers.ParseQuery(accepted.Headers.Location!.Query)["code"]));
        Assert.Equal(form["state"], QueryHelpers.ParseQuery(accepted.Headers.Location.Query)["state"].ToString());
        var again = await host.Client.GetAsync(host.AuthorizationUrl(new() { ["prompt"] = "none" }));
        Assert.False(string.IsNullOrWhiteSpace(QueryHelpers.ParseQuery(again.Headers.Location!.Query)["code"]));
        var forced = await host.Client.GetAsync(host.AuthorizationUrl(new() { ["prompt"] = "consent" }));
        Assert.Equal(HttpStatusCode.OK, forced.StatusCode);
    }

    [Theory]
    [InlineData("csrf")]
    [InlineData("scope")]
    [InlineData("nonce")]
    [InlineData("ticket")]
    [InlineData("deny")]
    public async Task MissingCsrfOrChangedDisplayedRequestNeverGrantsPermission(string change)
    {
        await using var host = await SsoProtocolTestHost.Create(); var form = await Form(host);
        switch (change)
        {
            case "csrf": form.Remove("__RequestVerificationToken"); break;
            case "scope": form["scope"] = "openid email"; break;
            case "nonce": form["nonce"] = "changed"; break;
            case "ticket": form["consent_ticket"] = "malformed"; break;
            case "deny": form["decision"] = "deny"; break;
        }
        var response = await host.Client.PostAsync("/connect/authorize", new FormUrlEncodedContent(form));
        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Redirect);
        if (response.Headers.Location != null) Assert.False(QueryHelpers.ParseQuery(response.Headers.Location.Query).ContainsKey("code"));
        Assert.Equal(0, await host.WithServices(s => s.GetRequiredService<AppDbContext>().MiniAppUserConsents.CountAsync()));
    }

    internal static async Task<Dictionary<string,string>> Form(SsoProtocolTestHost host)
    {
        host.Client.DefaultRequestHeaders.Remove("Cookie"); var cookie = await host.UserCookie(); host.Client.DefaultRequestHeaders.Add("Cookie", cookie);
        using var request = new HttpRequestMessage(HttpMethod.Get, host.AuthorizationUrl()); request.Headers.Accept.ParseAdd("application/json");
        var response = await host.Client.SendAsync(request); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var prompt = await response.Content.ReadFromJsonAsync<JsonElement>();
        var values = prompt.GetProperty("parameters").EnumerateObject().ToDictionary(x => x.Name, x => x.Value.GetString()!);
        values["__RequestVerificationToken"] = prompt.GetProperty("csrfToken").GetString()!;
        values["consent_ticket"] = prompt.GetProperty("consentTicket").GetString()!; values["decision"] = "allow";
        var csrf = response.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("__Host-AnktSsoCsrf="));
        host.Client.DefaultRequestHeaders.Remove("Cookie"); host.Client.DefaultRequestHeaders.Add("Cookie", cookie + "; " + csrf.Split(';')[0]);
        return values;
    }
}
