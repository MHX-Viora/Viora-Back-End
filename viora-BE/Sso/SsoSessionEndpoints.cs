using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Viora.Application.Developers;
using Viora.Infrastructure.Sso;

namespace viora_BE.Sso;

public sealed record SsoBootstrapRequest(Guid TransactionId);

public static class SsoSessionEndpoints
{
    public static void MapSsoSessionEndpoints(this WebApplication app)
    {
        app.MapGet("/sso/start", (string returnUrl, HttpContext context, SsoBootstrapService bootstrap, IOptions<SsoOptions> configured) => Guard(context, async () =>
        {
            var state = await bootstrap.StartAsync(returnUrl, context.RequestAborted);
            context.Response.Cookies.Append(BindingCookie(state.TransactionId), state.BrowserCredential,
                new CookieOptions { Secure = true, HttpOnly = true, SameSite = SameSiteMode.Lax, Path = "/", Expires = new DateTimeOffset(state.ExpiresAt) });
            return Results.Redirect(configured.Value.FirstPartyOrigin.TrimEnd('/') + "/sso/login?transaction=" + state.TransactionId);
        })).RequireRateLimiting("sso");

        app.MapPost("/sso/bootstrap", (SsoBootstrapRequest request, HttpContext context, SsoBootstrapService bootstrap, IOptions<SsoOptions> configured) => Guard(context, async () =>
        {
            if (!TrustedOrigin(context, configured.Value)) return Results.StatusCode(403);
            if (context.User.FindFirst("token_type")?.Value != "access" || !Guid.TryParse(context.User.FindFirst("sub")?.Value, out var account)) return Results.Unauthorized();
            if (!Guid.TryParse(context.User.FindFirst("sid")?.Value, out var social))
                return Results.Json(new { error = new { code = "SSO_REFRESH_REQUIRED", message = "Cần làm mới phiên ANKT trước khi đăng nhập ứng dụng." } }, statusCode: 401);
            await bootstrap.AuthenticateAsync(request.TransactionId, account, social, context.RequestAborted);
            return Results.Ok(new { completionUrl = new Uri(new Uri(configured.Value.Issuer), "/sso/complete?transaction=" + request.TransactionId).AbsoluteUri });
        })).RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme }).RequireCors("SsoBootstrap").RequireRateLimiting("sso");

        app.MapGet("/sso/complete", (Guid transaction, HttpContext context, SsoBootstrapService bootstrap) => Guard(context, async () =>
        {
            context.Request.Cookies.TryGetValue(BindingCookie(transaction), out var binding);
            var completed = await bootstrap.CompleteAsync(transaction, binding, context.RequestAborted);
            context.Response.Cookies.Append(SsoSessionHandler.CookieName, completed.Session.Credential,
                new CookieOptions { Secure = true, HttpOnly = true, SameSite = SameSiteMode.Lax, Path = "/", Expires = new DateTimeOffset(completed.Session.ExpiresAt) });
            context.Response.Cookies.Delete(BindingCookie(transaction), new CookieOptions { Secure = true, HttpOnly = true, SameSite = SameSiteMode.Lax, Path = "/" });
            return Results.LocalRedirect(completed.ReturnUrl);
        })).RequireRateLimiting("sso");
    }

    private static string BindingCookie(Guid transaction) => "__Host-AnktSsoBinding-" + transaction.ToString("N");
    public static bool TrustedOrigin(HttpContext context, SsoOptions options) => context.Request.Headers.Origin.Count == 1 &&
        string.Equals(context.Request.Headers.Origin.ToString(), options.FirstPartyOrigin.TrimEnd('/'), StringComparison.Ordinal);

    private static async Task<IResult> Guard(HttpContext context, Func<Task<IResult>> operation)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        try { return await operation(); }
        catch (DeveloperAuthException error) { return Results.Json(new { error = new { code = error.Code, message = error.Message } }, statusCode: error.Status); }
    }
}
