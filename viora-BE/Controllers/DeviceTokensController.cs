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
public sealed class DeviceTokensController(
    IMediator mediator,
    ILogger<DeviceTokensController> logger) : ControllerBase
{
    [HttpPost("register")]
    [HttpPost("~/api/device/register")]
    [ProducesResponseType<DeviceTokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterDeviceTokenRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Unauthorized(new DeviceTokenResponse(false, false, "Access token is missing user_id claim."));
        }

        if (!TryParsePlatform(request.Platform, out var platform))
        {
            return BadRequest(new DeviceTokenResponse(false, false, "Platform must be 0, 1, 2, 3, Android, Ios, Web, or Other."));
        }
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return BadRequest(new DeviceTokenResponse(false, false, "Token must not be empty."));
        }
        var token = request.Token;

        try
        {
            logger.LogInformation(
                "Register device token request received. UserId: {UserId}, DeviceId: {DeviceId}, Platform: {Platform}, TokenLength: {TokenLength}.",
                currentUserId,
                request.DeviceId,
                platform,
                token.Length);

            var response = await mediator.Send(new RegisterDeviceTokenCommand(
                currentUserId,
                token,
                request.DeviceId,
                request.DeviceName,
                platform,
                request.AppVersion), cancellationToken);

            return response.Success ? Ok(response) : BadRequest(response);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to register device token for user {UserId}. DeviceId: {DeviceId}, Platform: {Platform}.",
                currentUserId,
                request.DeviceId,
                platform);
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new DeviceTokenResponse(false, false, "Failed to register device token. Check server logs for details."));
        }
    }

    [HttpPost("unregister")]
    [HttpPost("~/api/device/unregister")]
    [ProducesResponseType<DeviceTokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Unregister(
        [FromBody] UnregisterDeviceTokenRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Unauthorized(new DeviceTokenResponse(false, false, "Access token is missing user_id claim."));
        }
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return BadRequest(new DeviceTokenResponse(false, false, "Token must not be empty."));
        }
        var token = request.Token;

        try
        {
            logger.LogInformation(
                "Unregister device token request received. UserId: {UserId}, TokenLength: {TokenLength}.",
                currentUserId,
                token.Length);

            var response = await mediator.Send(
                new UnregisterDeviceTokenCommand(currentUserId, token),
                cancellationToken);

            return response.Success ? Ok(response) : BadRequest(response);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to unregister device token for user {UserId}.", currentUserId);
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new DeviceTokenResponse(false, false, "Failed to unregister device token. Check server logs for details."));
        }
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
