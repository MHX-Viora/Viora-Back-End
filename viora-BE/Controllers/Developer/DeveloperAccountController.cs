using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Viora.Application.MiniApps;

namespace viora_BE.Controllers.Developer;

[ApiController, Authorize, Route("api/developer/account"), EnableRateLimiting("mini-app-launch")]
public sealed class DeveloperAccountController(IDeveloperAccountVerificationService verification) : ControllerBase
{
    private Guid AccountId => Guid.Parse(User.FindFirstValue("sub")!);
    [HttpGet("verification")]
    public Task<DeveloperAccountVerificationDto> Get(CancellationToken ct) => verification.GetAsync(AccountId, ct);
    [HttpPost("verify-email")]
    public Task<DeveloperAccountVerificationDto> VerifyEmail(DeveloperAccountEmailInput input, CancellationToken ct) => verification.VerifyEmailAsync(AccountId, input.FirebaseToken, ct);
}
public sealed record DeveloperAccountEmailInput([param: Required, MaxLength(10000)] string FirebaseToken);
