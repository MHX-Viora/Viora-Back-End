using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Viora.Application.Developers;

namespace viora_BE.Developer;

public sealed class DeveloperSessionHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    IDeveloperIdentityService identity) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DeveloperSession";
    public const string Policy = "DeveloperActive";
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith(SchemeName + " ", StringComparison.Ordinal)) return AuthenticateResult.NoResult();
        var session = await identity.AuthenticateAsync(header[(SchemeName.Length + 1)..], Context.RequestAborted);
        if (session is null) return AuthenticateResult.Fail("Invalid Developer session");
        var claims = new[] { new Claim("developer_id", session.Account.Id.ToString()), new Claim("developer_session", session.SessionId.ToString()), new Claim("developer_status", session.Account.Status) };
        return AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName));
    }
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;
        return Response.WriteAsJsonAsync(new { error = new { code = "UNAUTHENTICATED", message = "Vui lòng đăng nhập ANKT Developer." } });
    }
    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 403;
        return Response.WriteAsJsonAsync(new { error = new { code = "FORBIDDEN", message = "Tài khoản Developer chưa có quyền truy cập." } });
    }
}
