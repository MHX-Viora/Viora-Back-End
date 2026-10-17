using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Viora.Application.Developers;
using Viora.Infrastructure.Sso;

namespace viora_BE.Sso;

public sealed record SsoLinkAttestationRequest([Required, StringLength(100)] string Subject,
    [Required, StringLength(128)] string PartnerAccountId);
public sealed record SsoLinkProofRequest([Required, StringLength(43, MinimumLength = 43)] string Challenge, bool Approve = false);

[ApiController]
[Route("api/sso/account-links")]
[RequestSizeLimit(2048)]
[EnableRateLimiting("sso")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SsoAccountLinkController(SsoAccountLinkService links, SsoSessionService sessions, IOptions<SsoOptions> options) : ControllerBase
{
    [HttpPost("challenges")]
    public Task<IActionResult> Issue(SsoLinkAttestationRequest body, CancellationToken ct) => Guard(async () =>
    {
        // An attestation is sent by the authenticated partner backend, never browser JS.
        if (Request.Headers.ContainsKey("Origin") || Request.Headers.Authorization.Count != 1 ||
            !AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var authorization) ||
            !string.Equals(authorization.Scheme, "Basic", StringComparison.OrdinalIgnoreCase) || authorization.Parameter is not { Length: <= 4096 } encoded)
            return Unauthorized();
        string credentials;
        try { credentials = Encoding.UTF8.GetString(Convert.FromBase64String(encoded)); }
        catch (FormatException) { return Unauthorized(); }
        var separator = credentials.IndexOf(':');
        if (separator <= 0 || separator == credentials.Length - 1) return Unauthorized();
        return Ok(await links.IssueAsync(Uri.UnescapeDataString(credentials[..separator]), Uri.UnescapeDataString(credentials[(separator + 1)..]),
            body.Subject, body.PartnerAccountId, ct));
    });

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpPost("preview")]
    public Task<IActionResult> Preview(SsoLinkProofRequest body, CancellationToken ct) => Guard(async () =>
        await Owner(ct) is { } owner ? Ok(await links.PreviewAsync(owner, body.Challenge, ct)) : Unauthorized());

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpPost("confirm")]
    public Task<IActionResult> Confirm(SsoLinkProofRequest body, CancellationToken ct) => Guard(async () =>
    {
        if (await Owner(ct) is not { } owner) return Unauthorized();
        if (!body.Approve) return BadRequest(new { error = new { code = "SSO_LINK_APPROVAL_REQUIRED" } });
        return Ok(await links.ConfirmAsync(owner, body.Challenge, ct));
    });

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpGet("mine")]
    public Task<IActionResult> List(CancellationToken ct) => Guard(async () =>
        await Owner(ct) is { } owner ? Ok(await links.ListAsync(owner, ct)) : Unauthorized());

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpDelete("mine/{id:guid}")]
    public Task<IActionResult> Unlink(Guid id, CancellationToken ct) => Guard(async () =>
    {
        if (await Owner(ct) is not { } owner) return Unauthorized();
        return await links.UnlinkAsync(owner, id, ct) ? NoContent() : NotFound();
    });

    private async Task<Guid?> Owner(CancellationToken ct)
    {
        if (Request.Headers.ContainsKey("Origin") && !SsoSessionEndpoints.TrustedOrigin(HttpContext, options.Value)) return null;
        if (User.FindFirst("token_type")?.Value != "access" || !Guid.TryParse(User.FindFirst("sub")?.Value, out var owner) ||
            !Guid.TryParse(User.FindFirst("sid")?.Value, out var social)) return null;
        try { await sessions.ValidateSocialSessionAsync(owner, social, ct); return owner; }
        catch (DeveloperAuthException) { return null; }
    }

    private async Task<IActionResult> Guard(Func<Task<IActionResult>> operation)
    {
        Response.Headers.CacheControl = "no-store"; Response.Headers["Referrer-Policy"] = "no-referrer";
        try { return await operation(); }
        catch (DeveloperAuthException error) { return StatusCode(error.Status, new { error = new { code = error.Code, message = error.Message } }); }
        catch (DbUpdateException) { return Conflict(new { error = new { code = "SSO_LINK_CONFLICT" } }); }
        catch (Npgsql.PostgresException error) when (error.SqlState is "40001" or "40P01") { return Conflict(new { error = new { code = "SSO_LINK_CONFLICT" } }); }
    }
}
