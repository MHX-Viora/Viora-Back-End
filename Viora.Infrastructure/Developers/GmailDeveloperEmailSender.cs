using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using MimeKit;
using Viora.Application.Developers;

namespace Viora.Infrastructure.Developers;

public sealed class GmailDeveloperEmailSender(HttpClient client, IOptions<DeveloperAuthOptions> options) : IDeveloperEmailSender
{
    private DeveloperAuthOptions Config => options.Value;
    public bool IsConfigured => MailboxAddress.TryParse(Config.GmailFrom, out _) &&
        !string.IsNullOrWhiteSpace(Config.GmailClientId) &&
        !string.IsNullOrWhiteSpace(Config.GmailClientSecret) &&
        !string.IsNullOrWhiteSpace(Config.GmailRefreshToken);

    public async Task SendAsync(string recipient, string subject, string text, CancellationToken ct)
    {
        if (!IsConfigured) throw new InvalidOperationException("Developer Gmail API is not configured.");
        using var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(Config.GmailFrom));
        message.To.Add(MailboxAddress.Parse(recipient));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = text };
        using var stream = new MemoryStream();
        await message.WriteToAsync(stream, ct);

        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = Config.GmailClientId,
                ["client_secret"] = Config.GmailClientSecret,
                ["refresh_token"] = Config.GmailRefreshToken,
                ["grant_type"] = "refresh_token"
            })
        };
        using var tokenResponse = await client.SendAsync(tokenRequest, ct);
        if (!tokenResponse.IsSuccessStatusCode)
            throw new InvalidOperationException($"Developer Gmail token exchange failed (HTTP {(int)tokenResponse.StatusCode}).");
        string accessToken;
        try
        {
            using var token = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync(ct));
            if (token.RootElement.ValueKind != JsonValueKind.Object ||
                !token.RootElement.TryGetProperty("access_token", out var value) || value.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(value.GetString()))
                throw new InvalidOperationException("Developer Gmail token response is invalid.");
            accessToken = value.GetString()!;
        }
        catch (JsonException)
        {
            // Provider bodies can contain secrets; never attach them or inner exceptions.
            throw new InvalidOperationException("Developer Gmail token response is invalid.");
        }
        if (accessToken.Any(char.IsWhiteSpace) || accessToken.Any(char.IsControl))
            throw new InvalidOperationException("Developer Gmail token response is invalid.");

        using var sendRequest = new HttpRequestMessage(HttpMethod.Post, "https://gmail.googleapis.com/gmail/v1/users/me/messages/send")
        {
            Content = JsonContent.Create(new { raw = WebEncoders.Base64UrlEncode(stream.ToArray()) })
        };
        sendRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(sendRequest, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Developer Gmail delivery failed (HTTP {(int)response.StatusCode}).");
    }
}
