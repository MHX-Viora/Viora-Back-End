using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Viora.Application.Accounts;

namespace viora_BE.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public sealed class AuthController(IMediator mediator) : ControllerBase
{
    [HttpGet("forgot-password/status")]
    [ProducesResponseType<ForgotPasswordStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ForgotPasswordErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ForgotPasswordStatusResponse>> GetForgotPasswordStatus(
        [FromQuery, Required, MaxLength(255)] string identifier,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new GetForgotPasswordStatusQuery(identifier),
            cancellationToken);
        return result.Outcome == ForgotPasswordOutcome.Success
            ? Ok(result.Value)
            : ToError<ForgotPasswordStatusResponse>(result);
    }

    [HttpPut("phone-number")]
    [Authorize]
    [ProducesResponseType<ForgotPasswordMessageResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ForgotPasswordMessageResponse>> SetPhoneNumber(
        SetForgotPasswordPhoneRequest request,
        CancellationToken cancellationToken)
    {
        var accountIdValue =
            User.FindFirstValue(ClaimTypes.NameIdentifier) ??
            User.FindFirstValue("sub");
        if (!Guid.TryParse(accountIdValue, out var accountId) ||
            accountId != request.UserId)
        {
            return Unauthorized(new ForgotPasswordErrorResponse(
                false,
                "Bạn không có quyền cập nhật số điện thoại cho tài khoản này."));
        }

        var result = await mediator.Send(
            new SetForgotPasswordPhoneCommand(
                request.UserId,
                request.PhoneNumber ?? string.Empty,
                request.FirebaseToken ?? string.Empty),
            cancellationToken);
        return result.Outcome == ForgotPasswordOutcome.Success
            ? Ok(result.Value)
            : ToError<ForgotPasswordMessageResponse>(result);
    }

    [HttpPost("reset-password")]
    [ProducesResponseType<ForgotPasswordMessageResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ForgotPasswordMessageResponse>> ResetPassword(
        ResetForgottenPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new ResetForgottenPasswordCommand(
                request.FirebaseToken ?? string.Empty,
                request.NewPassword ?? string.Empty),
            cancellationToken);
        return result.Outcome == ForgotPasswordOutcome.Success
            ? Ok(result.Value)
            : ToError<ForgotPasswordMessageResponse>(result);
    }

    private ActionResult<T> ToError<T>(ForgotPasswordResult<T> result)
    {
        var status = result.Outcome switch
        {
            ForgotPasswordOutcome.NotFound => StatusCodes.Status404NotFound,
            ForgotPasswordOutcome.Unauthorized => StatusCodes.Status401Unauthorized,
            ForgotPasswordOutcome.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };
        return StatusCode(
            status,
            new ForgotPasswordErrorResponse(false, result.Message, result.Code));
    }
}

public sealed record SetForgotPasswordPhoneRequest(
    Guid UserId,
    [property: MaxLength(20)] string? PhoneNumber,
    [property: MaxLength(4096)] string? FirebaseToken);

public sealed record ResetForgottenPasswordRequest(
    [property: MaxLength(4096)] string? FirebaseToken,
    [property: StringLength(100, MinimumLength = 8)] string? NewPassword);

public sealed record ForgotPasswordErrorResponse(
    bool Success,
    string Message,
    string? Code = null);
