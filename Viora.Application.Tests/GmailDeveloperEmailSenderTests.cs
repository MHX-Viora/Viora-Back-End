using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MimeKit;
using Viora.Application.Developers;
using Viora.Infrastructure.Developers;
using viora_BE.Developer;
using Xunit;

public sealed class GmailDeveloperEmailSenderTests
{
    [Theory]
    [InlineData(null, typeof(SmtpDeveloperEmailSender))]
    [InlineData("Smtp", typeof(SmtpDeveloperEmailSender))]
    [InlineData("GmailApi", typeof(GmailDeveloperEmailSender))]
    public void DependencyInjectionSelectsProviderWithoutChangingSmtpDefault(string? selection, Type expected)
    {
        var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder();
        if (selection is not null) builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["DeveloperAuth:EmailProvider"] = selection });
        builder.Services.AddDeveloperAccounts(builder.Configuration);
        using var services = builder.Services.BuildServiceProvider();
        using var scope = services.CreateScope();
        Assert.IsType(expected, scope.ServiceProvider.GetRequiredService<IDeveloperEmailSender>());
    }

    private static DeveloperAuthOptions Config() => new()
    {
        GmailFrom = "sender@gmail.com", GmailClientId = "client-id",
        GmailClientSecret = "client-secret", GmailRefreshToken = "refresh-secret"
    };

    [Theory]
    [InlineData("GmailFrom")]
    [InlineData("GmailClientId")]
    [InlineData("GmailClientSecret")]
    [InlineData("GmailRefreshToken")]
    public async Task MissingConfigurationNeverCallsGoogle(string property)
    {
        var config = Config(); typeof(DeveloperAuthOptions).GetProperty(property)!.SetValue(config, "");
        var handler = new Transport((_, _) => throw new Exception("Must not contact Google"));
        using var client = new HttpClient(handler);
        var sender = new GmailDeveloperEmailSender(client, Options.Create(config));
        Assert.False(sender.IsConfigured);
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync("dev@example.test", "Subject", "Text", default));
    }

    [Fact]
    public async Task RefreshesTokenAndSendsUnicodeMimeOverHttps()
    {
        var calls = 0;
        var handler = new Transport(async (request, ct) =>
        {
            calls++;
            Assert.Equal(HttpMethod.Post, request.Method);
            if (calls == 1)
            {
                Assert.Equal("https://oauth2.googleapis.com/token", request.RequestUri!.ToString());
                var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(ct));
                Assert.Equal("refresh_token", form["grant_type"].ToString());
                Assert.Equal("refresh-secret", form["refresh_token"].ToString());
                Assert.Equal("client-secret", form["client_secret"].ToString());
                return Json("{\"access_token\":\"access-secret\",\"expires_in\":3600}");
            }
            Assert.Equal("https://gmail.googleapis.com/gmail/v1/users/me/messages/send", request.RequestUri!.ToString());
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("access-secret", request.Headers.Authorization.Parameter);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var raw = body.RootElement.GetProperty("raw").GetString()!;
            Assert.DoesNotContain("=", raw);
            using var stream = new MemoryStream(WebEncoders.Base64UrlDecode(raw));
            using var message = await MimeMessage.LoadAsync(stream, ct);
            Assert.Equal("sender@gmail.com", Assert.Single(message.From.Mailboxes).Address);
            Assert.Equal("dev@example.test", Assert.Single(message.To.Mailboxes).Address);
            Assert.Equal("Xác minh email", message.Subject);
            Assert.Contains("Liên kết xác minh", message.TextBody);
            return Json("{\"id\":\"message-id\"}");
        });
        using var client = new HttpClient(handler);
        var sender = new GmailDeveloperEmailSender(client, Options.Create(Config()));
        Assert.True(sender.IsConfigured);
        await sender.SendAsync("dev@example.test", "Xác minh email", "Liên kết xác minh", default);
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProviderErrorsDoNotExposeGoogleResponseOrCredentials(bool tokenFailure)
    {
        var calls = 0;
        var handler = new Transport((_, _) => Task.FromResult(++calls == 1 && !tokenFailure
            ? Json("{\"access_token\":\"access-secret\"}")
            : new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("refresh-secret recipient@example.test") }));
        using var client = new HttpClient(handler);
        var sender = new GmailDeveloperEmailSender(client, Options.Create(Config()));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync("dev@example.test", "Subject", "Text", default));
        Assert.DoesNotContain("secret", error.ToString());
        Assert.DoesNotContain("recipient@example.test", error.ToString());
        Assert.Equal(tokenFailure ? 1 : 2, calls);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not json refresh-secret")]
    [InlineData("[]")]
    [InlineData("{\"access_token\":17}")]
    [InlineData("{\"access_token\":\"bad\\r\\nheader\"}")]
    public async Task InvalidAccessTokenStopsBeforeSending(string response)
    {
        var calls = 0;
        using var client = new HttpClient(new Transport((_, _) => { calls++; return Task.FromResult(Json(response)); }));
        var sender = new GmailDeveloperEmailSender(client, Options.Create(Config()));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync("dev@example.test", "Subject", "Text", default));
        Assert.DoesNotContain("secret", error.ToString());
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task CancellationPropagates()
    {
        using var client = new HttpClient(new Transport((_, ct) => { ct.ThrowIfCancellationRequested(); return Task.FromResult(Json("{}")); }));
        var sender = new GmailDeveloperEmailSender(client, Options.Create(Config()));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sender.SendAsync("dev@example.test", "Subject", "Text", cancellation.Token));
    }

    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value) };
    private sealed class Transport(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request, ct);
    }
}
