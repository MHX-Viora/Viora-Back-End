using MimeKit;
using Viora.Application.Developers;

namespace Viora.Infrastructure.Developers;

public static class DeveloperEmailMessage
{
    public static MimeMessage Create(string sender, string recipient, string subject, DeveloperEmailContent content)
    {
        var from = MailboxAddress.Parse(sender);
        from.Name = "ANKT";
        var message = new MimeMessage();
        message.From.Add(from);
        message.To.Add(MailboxAddress.Parse(recipient));
        message.Subject = subject;
        message.Body = new BodyBuilder { TextBody = content.Text, HtmlBody = content.Html }.ToMessageBody();
        return message;
    }
}
