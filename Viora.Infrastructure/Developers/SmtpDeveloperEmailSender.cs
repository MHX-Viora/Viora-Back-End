using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MimeKit;
using Viora.Application.Developers;

namespace Viora.Infrastructure.Developers;

public sealed class SmtpDeveloperEmailSender(IOptions<DeveloperAuthOptions> options, IHostEnvironment environment) : IDeveloperEmailSender
{
    private DeveloperAuthOptions Config => options.Value;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Config.SmtpHost) &&
        MailboxAddress.TryParse(Config.SmtpFrom, out _) && Config.SmtpPort is > 0 and <= 65535;

    public async Task SendAsync(string recipient, string subject, string text, CancellationToken ct)
    {
        if (!IsConfigured) throw new InvalidOperationException("Developer SMTP is not configured.");
        var insecure = environment.IsDevelopment() && Config.AllowInsecureLocalSmtp &&
            Config.SmtpHost is "localhost" or "127.0.0.1" or "::1";
        using var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(Config.SmtpFrom));
        message.To.Add(MailboxAddress.Parse(recipient)); message.Subject = subject;
        message.Body = new TextPart("plain") { Text = text };
        using var client = new SmtpClient { Timeout = 10000 };
        await client.ConnectAsync(Config.SmtpHost, Config.SmtpPort,
            insecure ? SecureSocketOptions.None : Config.SmtpImplicitTls ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, ct);
        if (!string.IsNullOrWhiteSpace(Config.SmtpUser)) await client.AuthenticateAsync(Config.SmtpUser, Config.SmtpPassword, ct);
        await client.SendAsync(message, ct); await client.DisconnectAsync(true, ct);
    }
}
