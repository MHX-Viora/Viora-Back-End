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

    public Task SendAsync(string recipient, string subject, string text, CancellationToken ct) =>
        SendAsync(recipient, subject, new DeveloperEmailContent(text), ct);

    public async Task SendAsync(string recipient, string subject, DeveloperEmailContent content, CancellationToken ct)
    {
        if (!IsConfigured) throw new InvalidOperationException("Developer SMTP is not configured.");
        var insecure = environment.IsDevelopment() && Config.AllowInsecureLocalSmtp &&
            Config.SmtpHost is "localhost" or "127.0.0.1" or "::1";
        using var message = DeveloperEmailMessage.Create(Config.SmtpFrom, recipient, subject, content);
        using var client = new SmtpClient { Timeout = 10000 };
        await client.ConnectAsync(Config.SmtpHost, Config.SmtpPort,
            insecure ? SecureSocketOptions.None : Config.SmtpImplicitTls ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, ct);
        if (!string.IsNullOrWhiteSpace(Config.SmtpUser)) await client.AuthenticateAsync(Config.SmtpUser, Config.SmtpPassword, ct);
        await client.SendAsync(message, ct); await client.DisconnectAsync(true, ct);
    }
}
