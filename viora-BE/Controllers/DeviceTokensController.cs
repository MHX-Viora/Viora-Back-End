using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Realtime;
using Viora.Domain.Entities;

namespace viora_BE.Controllers;

[ApiController]
[Route("api/device-token")]
[Authorize]
public sealed class DeviceTokensController(IMediator mediator) : ControllerBase
{
    [HttpPost("register")]
    [ProducesResponseType<DeviceTokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Register(
        RegisterDeviceTokenRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId)) return Unauthorized();

        var response = await mediator.Send(new RegisterDeviceTokenCommand(
            currentUserId,
            request.Token,
            request.DeviceId,
            request.DeviceName,
            request.Platform,
            request.AppVersion), cancellationToken);

        return response.Success ? Ok(response) : BadRequest(response);
    }

    [HttpPost("unregister")]
    [ProducesResponseType<DeviceTokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Unregister(
        UnregisterDeviceTokenRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId)) return Unauthorized();

        var response = await mediator.Send(
            new UnregisterDeviceTokenCommand(currentUserId, request.Token),
            cancellationToken);

        return response.Success ? Ok(response) : BadRequest(response);
    }

    private bool TryGetCurrentUserId(out Guid userId)
    {
        var value = User.FindFirstValue("user_id");
        return Guid.TryParse(value, out userId);
    }
}

public sealed record RegisterDeviceTokenRequest(
    [param: Required] string Token,
    string? DeviceId,
    string? DeviceName,
    DevicePlatform Platform,
    string? AppVersion);

public sealed record UnregisterDeviceTokenRequest([param: Required] string Token);
