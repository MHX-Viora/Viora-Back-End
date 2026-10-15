namespace Viora.Infrastructure.Developers;

public sealed class DeveloperAuthOptions
{
    public string PortalUrl { get; set; } = "";
    public string BffSharedSecret { get; set; } = "";
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public string SmtpUser { get; set; } = "";
    public string SmtpPassword { get; set; } = "";
    public string SmtpFrom { get; set; } = "";
    public bool SmtpImplicitTls { get; set; }
    // Only honored in Development and only for a loopback SMTP sink.
    public bool AllowInsecureLocalSmtp { get; set; }
}
