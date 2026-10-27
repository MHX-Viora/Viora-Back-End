using System.Net;
using System.Text.RegularExpressions;
using Viora.Application.Developers;
using Viora.Infrastructure.Developers;
using Xunit;

public sealed class DeveloperEmailTemplateTests
{
    private const string Link = "https://mxh.ankt.vn/developers/verify-email#token=synthetic-preview-token";

    [Fact]
    public void VerificationRendersVietnameseBrandingAndBothLinks()
    {
        var content = DeveloperEmailTemplates.Verification("Nguyễn An", Link, 2026);
        Assert.Contains("Xác minh địa chỉ email", WebUtility.HtmlDecode(content.Html));
        Assert.Contains("Xác minh email của tôi", WebUtility.HtmlDecode(content.Html));
        Assert.Contains("Nguyễn An", WebUtility.HtmlDecode(content.Html));
        Assert.Contains("#9D0000", content.Html);
        Assert.Contains("#101827", content.Html);
        Assert.Contains("© 2026 ANKT Developer Platform", WebUtility.HtmlDecode(content.Html));
        var actions = Regex.Matches(content.Html!, "href=\"([^\"]+)\"").Select(m => WebUtility.HtmlDecode(m.Groups[1].Value));
        Assert.Equal(2, actions.Count(url => url == Link));
        Assert.Contains(Link, content.Text);
        Assert.Contains("chỉ sử dụng một lần", content.Text);
        Assert.Contains("không chia sẻ", content.Text);
        Assert.Contains("bỏ qua", content.Text);
        Assert.DoesNotContain("<table", content.Text);
    }

    [Fact]
    public void DynamicNameAndUrlsAreEncodedWithoutRecursiveSubstitution()
    {
        var name = "<img src=x onerror=alert(1)> & {{title}}";
        var link = Link.Replace("#", "?source=one&next=\"quoted\"#");
        var content = DeveloperEmailTemplates.Verification(name, link, 2026);
        Assert.DoesNotContain("<img", content.Html);
        Assert.Contains("&lt;img", content.Html);
        Assert.Contains("{{title}}", content.Html);
        Assert.Contains("&amp;next=&quot;quoted&quot;", content.Html);
        Assert.Contains(name, content.Text);
        Assert.Equal(2, Regex.Matches(content.Html!, "href=\"([^\"]+)\"")
            .Count(m => WebUtility.HtmlDecode(m.Groups[1].Value) == link));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,hello")]
    [InlineData("http://mxh.ankt.vn/developers/verify-email#token=example")]
    public void UnsafeActionUrlsAreRejected(string link) =>
        Assert.Throws<ArgumentException>(() => DeveloperEmailTemplates.Verification("An", link, 2026));

    [Fact]
    public void LocalhostLinkRemainsUnchangedAndResetUsesSameLayout()
    {
        const string link = "http://localhost:8081/developers/reset-password#token=synthetic-token";
        var content = DeveloperEmailTemplates.PasswordReset("An", link, 2026);
        Assert.Contains("Đặt lại mật khẩu", WebUtility.HtmlDecode(content.Html));
        Assert.Contains(link, content.Text);
        Assert.Contains("href=\"" + link + "\"", content.Html);
        Assert.Contains("30 phút", content.Text);
        Assert.DoesNotContain("<script", content.Html);
        Assert.DoesNotContain("<style", content.Html);
        Assert.DoesNotContain("<link", content.Html);
        Assert.Contains("role=\"presentation\"", content.Html);
        Assert.Contains("max-width:600px", content.Html);
    }

    [Fact]
    public void MimeHasPlainTextFirstThenHtmlAndPreservesAuthorizedMailbox()
    {
        var content = DeveloperEmailTemplates.Verification("An", Link, 2026);
        using var message = DeveloperEmailMessage.Create("Previous Name <mxh@baochi.online>", "dev@example.test", "Xác minh email", content);
        var from = Assert.Single(message.From.Mailboxes);
        Assert.Equal("mxh@baochi.online", from.Address);
        Assert.Equal("ANKT", from.Name);
        var alternative = Assert.IsType<MimeKit.MultipartAlternative>(message.Body);
        Assert.Equal("text/plain", alternative[0].ContentType.MimeType);
        Assert.Equal("text/html", alternative[1].ContentType.MimeType);
        Assert.Equal(content.Text.ReplaceLineEndings("\n"), message.TextBody!.ReplaceLineEndings("\n"));
        Assert.Equal(content.Html!.ReplaceLineEndings("\n"), message.HtmlBody!.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void SharedLayoutEncodesAllCopySlotsAndContainsNoOrangeIconOrExternalAssets()
    {
        var copy = "<script>alert(1)</script> & \"quote\"";
        var content = DeveloperEmailTemplates.Render(new(copy, copy, copy, copy, copy, Link, copy, 2026));
        Assert.DoesNotContain("<script", content.Html);
        Assert.DoesNotContain("<img", content.Html);
        Assert.DoesNotContain("<svg", content.Html);
        Assert.DoesNotContain("#F97316", content.Html);
        Assert.Contains("#9D0000", content.Html);
        Assert.Contains(WebUtility.HtmlEncode(copy), content.Html);
        Assert.DoesNotContain("{{actionUrl}}", content.Html);
        Assert.DoesNotContain("<style", content.Html);
        Assert.DoesNotContain("<link", content.Html);
    }
}
