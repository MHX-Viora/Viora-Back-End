using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
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
    [HttpPost("~/api/device/register")]
    [ProducesResponseType<DeviceTokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterDeviceTokenRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId)) return Unauthorized();
        if (!TryParsePlatform(request.Platform, out var platform))
        {
            return BadRequest(new DeviceTokenResponse(false, false, "Platform must be 0, 1, 2, 3, Android, Ios, Web, or Other."));
        }

        var response = await mediator.Send(new RegisterDeviceTokenCommand(
            currentUserId,
            request.Token,
            request.DeviceId,
            request.DeviceName,
            platform,
            request.AppVersion), cancellationToken);

        return response.Success ? Ok(response) : BadRequest(response);
    }

    [HttpPost("unregister")]
    [HttpPost("~/api/device/unregister")]
    [ProducesResponseType<DeviceTokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Unregister(
        [FromBody] UnregisterDeviceTokenRequest request,
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

    private static bool TryParsePlatform(JsonElement value, out DevicePlatform platform)
    {
        if (value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt16(out var number) &&
            Enum.IsDefined(typeof(DevicePlatform), number))
        {
            platform = (DevicePlatform)number;
            return true;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            if (string.Equals(text, "ios", StringComparison.OrdinalIgnoreCase))
            {
                platform = DevicePlatform.Ios;
                return true;
            }

            if (Enum.TryParse<DevicePlatform>(text, ignoreCase: true, out platform))
            {
                return true;
            }
        }

        platform = default;
        return false;
    }
}

public sealed record RegisterDeviceTokenRequest(
    [param: Required] string Token,
    string? DeviceId,
    string? DeviceName,
    JsonElement Platform,
    string? AppVersion);

public sealed record UnregisterDeviceTokenRequest([param: Required] string Token);
