using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Viora.Application.Developers;
using Viora.Infrastructure.Developers;
using viora_BE.Developer;
using System.Text.Encodings.Web;

namespace viora_BE.Sso;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SandboxTestController(SandboxTestService tests, IOptions<SandboxTestOptions> configured) : ControllerBase
{
    [HttpPost("~/sso/sandbox-test/start")]
    [EnableRateLimiting("sso")]
    [RequestSizeLimit(1024)]
    public async Task<IActionResult> Start(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";
        if (!configured.Value.Enabled || !configured.Value.SsoEnabled ||
            !Uri.TryCreate(configured.Value.Issuer, UriKind.Absolute, out var issuer) ||
            !Request.IsHttps || !string.Equals(Request.Host.Value, issuer.Authority, StringComparison.OrdinalIgnoreCase) || Request.Query.Count != 0 ||
            Request.Headers.Origin.Count != 1 || !configured.Value.AllowsPortal(Request.Headers.Origin.ToString()) ||
            !Request.HasFormContentType) return BadRequest();
        var form = await Request.ReadFormAsync(ct);
        if (form.Count != 1 || form["launch_ticket"].Count != 1 || form.Files.Count != 0) return BadRequest();
        try
        {
            var launch = await tests.ConsumeAsync(form["launch_ticket"], ct);
            Response.Cookies.Append(SandboxTestApi.ClientCookie(launch.ClientId), launch.Credential, new CookieOptions
            { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, Path = "/", Expires = new DateTimeOffset(DateTime.SpecifyKind(launch.ExpiresAt, DateTimeKind.Utc)) });
            // Chromium applies a submitting document's form-action to HTTP
            // redirects too. Complete the issuer POST first, then navigate the
            // registered partner from this new, token-free document.
            var website = HtmlEncoder.Default.Encode(launch.Website);
            return Content("<!doctype html><html lang=vi><meta charset=utf-8><meta http-equiv=refresh content=\"0;url=" + website +
                "\"><title>Đang mở Mini App</title><p>Đang mở Mini App…</p><a href=\"" + website + "\">Tiếp tục</a></html>", "text/html; charset=utf-8");
        }
        catch (DeveloperAuthException e) { return StatusCode(e.Status, new { error = new { code = e.Code, message = e.Message } }); }
    }
}
