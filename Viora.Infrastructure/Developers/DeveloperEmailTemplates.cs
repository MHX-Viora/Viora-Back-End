using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Viora.Application.Developers;

namespace Viora.Infrastructure.Developers;

// Data is plain text, never caller-supplied HTML. New event templates reuse this model/layout.
public sealed record DeveloperActionEmailTemplate(string Title, string Preview, string RecipientName,
    string Introduction, string ActionLabel, string ActionUrl, string SecurityNotice, int Year);

public static class DeveloperEmailTemplates
{
    private static readonly Lazy<string> Layout = new(() =>
    {
        using var stream = typeof(DeveloperEmailTemplates).Assembly.GetManifestResourceStream(
            "Viora.Infrastructure.Developers.EmailTemplates.DeveloperActionEmail.html")
            ?? throw new InvalidOperationException("Developer email layout is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    public static DeveloperEmailContent Verification(string name, string link, int year) => Render(new(
        "Xác minh địa chỉ email", "Chỉ một bước nữa để bắt đầu với ANKT Developer.", name,
        "Cảm ơn bạn đã đăng ký ANKT Developer. Vui lòng xác minh địa chỉ email để kích hoạt tài khoản và bắt đầu xây dựng ứng dụng cùng ANKT.",
        "Xác minh email của tôi", link,
        "Liên kết có hiệu lực trong 24 giờ và chỉ sử dụng một lần. Vui lòng không chia sẻ liên kết này với bất kỳ ai. Nếu bạn không yêu cầu tạo tài khoản, hãy bỏ qua email này.", year));

    public static DeveloperEmailContent PasswordReset(string name, string link, int year) => Render(new(
        "Đặt lại mật khẩu", "Hướng dẫn đặt lại mật khẩu tài khoản ANKT Developer.", name,
        "Chúng tôi nhận được yêu cầu đặt lại mật khẩu tài khoản ANKT Developer của bạn. Nhấn nút bên dưới để chọn mật khẩu mới.",
        "Đặt lại mật khẩu của tôi", link,
        "Liên kết có hiệu lực trong 30 phút và chỉ sử dụng một lần. Vui lòng không chia sẻ liên kết này với bất kỳ ai. Nếu bạn không yêu cầu đặt lại mật khẩu, hãy bỏ qua email này; mật khẩu hiện tại vẫn được giữ nguyên.", year));

    public static DeveloperEmailContent Render(DeveloperActionEmailTemplate template)
    {
        if (!Uri.TryCreate(template.ActionUrl, UriKind.Absolute, out var url) ||
            (url.Scheme != "https" && !(url.Scheme == "http" && url.IsLoopback)) ||
            url.UserInfo.Length != 0 || template.ActionUrl.Any(char.IsControl))
            throw new ArgumentException("Developer email action URL must use HTTPS or loopback HTTP.", nameof(template));
        var greeting = string.IsNullOrWhiteSpace(template.RecipientName) ? "Xin chào bạn," : $"Xin chào {template.RecipientName},";
        var values = new Dictionary<string, string>
        {
            ["title"] = template.Title, ["preview"] = template.Preview, ["greeting"] = greeting,
            ["introduction"] = template.Introduction, ["actionLabel"] = template.ActionLabel,
            ["actionUrl"] = template.ActionUrl, ["securityNotice"] = template.SecurityNotice,
            ["year"] = template.Year.ToString(CultureInfo.InvariantCulture)
        };
        // One pass prevents user text containing {{slots}} from being interpreted again.
        var html = Regex.Replace(Layout.Value, @"\{\{(\w+)\}\}", match => WebUtility.HtmlEncode(values[match.Groups[1].Value]));
        var text = $"ANKT Developer\n\n{template.Title}\n\n{greeting}\n\n{template.Introduction}\n\n{template.ActionLabel}:\n{template.ActionUrl}\n\n{template.SecurityNotice}\n\nANKT Developer Platform\nhttps://mxh.ankt.vn/developers\n© {template.Year} ANKT Developer Platform. Bảo lưu mọi quyền.";
        return new(text, html);
    }
}
