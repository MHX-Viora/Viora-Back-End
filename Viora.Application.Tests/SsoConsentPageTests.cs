using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class SsoConsentPageTests
{
    [Theory]
    [InlineData("decision", "allow")]
    [InlineData("consent_ticket", "attacker")]
    [InlineData("__RequestVerificationToken", "attacker")]
    public async Task UntrustedQueryCannotSupplyConsentFormControls(string name, string value)
    {
        await using var host = await SsoProtocolTestHost.Create();
        host.Client.DefaultRequestHeaders.Add("Cookie", await host.UserCookie());
        var response = await host.Client.GetAsync(host.AuthorizationUrl(new() { [name] = value }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain($"type=\"hidden\" name=\"{name}\" value=\"{value}\"", html);
    }

    [Fact]
    public async Task BrowserReceivesAccessibleEncodedConsentFormWithSecurityHeaders()
    {
        await using var host = await SsoProtocolTestHost.Create();
        await host.WithServices(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            (await db.MiniApps.SingleAsync()).Name = "<script>alert(1)</script>";
            await db.SaveChangesAsync(); return true;
        });
        host.Client.DefaultRequestHeaders.Add("Cookie", await host.UserCookie());
        var response = await host.Client.GetAsync(host.AuthorizationUrl());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("<html lang=\"vi\"", html); Assert.Contains("<main", html);
        Assert.Contains("&lt;script&gt;", html); Assert.DoesNotContain("<script>alert", html);
        Assert.Contains("name=\"decision\" value=\"allow\"", html);
        Assert.Contains("name=\"decision\" value=\"deny\"", html);
        Assert.Contains("name=\"consent_ticket\"", html);
        Assert.Contains("name=\"__RequestVerificationToken\"", html);
        Assert.Contains("Protocol test", html); Assert.Contains("profile", html);
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
        var policy = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("form-action 'self' https://partner.example.com;", policy);
        Assert.DoesNotContain("form-action *", policy);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
    }
}
