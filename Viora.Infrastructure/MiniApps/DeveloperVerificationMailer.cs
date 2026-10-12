using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using Viora.Application.MiniApps;
namespace Viora.Infrastructure.MiniApps;
public sealed class DeveloperVerificationOptions
{
    public string OtpHashKey { get; set; } = "";
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public string SmtpUsername { get; set; } = "";
    public string SmtpPassword { get; set; } = "";
    public string SenderEmail { get; set; } = "";
}
public sealed class DeveloperVerificationMailer(IOptions<DeveloperVerificationOptions> options) : IDeveloperVerificationMailer
{
    public async Task SendCodeAsync(string email,string code,CancellationToken ct)
    {
        var o=options.Value;
        if(string.IsNullOrWhiteSpace(o.SmtpHost)||!MailAddress.TryCreate(o.SenderEmail,out _))
            throw new MiniAppException("EMAIL_DELIVERY_UNAVAILABLE","Email verification delivery has not been configured.",503);
        using var smtp=new SmtpClient(o.SmtpHost,o.SmtpPort) { EnableSsl=true,Timeout=15000,UseDefaultCredentials=false };
        if(!string.IsNullOrEmpty(o.SmtpUsername)) smtp.Credentials=new NetworkCredential(o.SmtpUsername,o.SmtpPassword);
        using var message=new MailMessage(o.SenderEmail,email,"ANKT - Xác minh email Developer",$"Mã xác minh của bạn: {code}. Mã có hiệu lực 10 phút. Không chia sẻ mã này.")
        { SubjectEncoding=System.Text.Encoding.UTF8, BodyEncoding=System.Text.Encoding.UTF8 };
        try { await smtp.SendMailAsync(message,ct); }
        catch(SmtpException) { throw new MiniAppException("EMAIL_DELIVERY_FAILED","Could not send verification email. Please retry later.",503); }
    }
}
