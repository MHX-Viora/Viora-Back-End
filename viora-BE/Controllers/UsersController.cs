using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Users;
using Viora.Domain.Entities;

namespace viora_BE.Controllers;

[ApiController]
[Route("api/users/profile")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class UsersController(IUserProfileService userProfileService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<UserResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<UserProfileErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<UserProfileErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<UserProfileErrorResponse>(StatusCodes.Status409Conflict)]
    public Task<ActionResult<UserResponse>> Create(
        SaveUserProfileRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(request, true, cancellationToken);

    [HttpPut]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<UserProfileErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<UserProfileErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<UserProfileErrorResponse>(StatusCodes.Status404NotFound)]
    public Task<ActionResult<UserResponse>> Update(
        SaveUserProfileRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(request, false, cancellationToken);

    private async Task<ActionResult<UserResponse>> ExecuteAsync(
        SaveUserProfileRequest request,
        bool create,
        CancellationToken cancellationToken)
    {
        var subject = User.FindFirstValue("sub");
        if (!Guid.TryParse(subject, out var accountId))
        {
            return Unauthorized(new UserProfileErrorResponse("Token đăng nhập không hợp lệ."));
        }

        try
        {
            var command = new SaveUserProfileCommand(
                request.DisplayName,
                request.AvatarUrl,
                request.CoverUrl,
                request.Gender);
            var user = create
                ? await userProfileService.CreateAsync(accountId, command, cancellationToken)
                : await userProfileService.UpdateAsync(accountId, command, cancellationToken);

            return create
                ? StatusCode(StatusCodes.Status201Created, user)
                : Ok(user);
        }
        catch (UserProfileException exception)
        {
            var status = exception.Code switch
            {
                UserProfileError.ProfileAlreadyExists => StatusCodes.Status409Conflict,
                UserProfileError.ProfileNotFound => StatusCodes.Status404NotFound,
                UserProfileError.AccountUnavailable => StatusCodes.Status403Forbidden,
                _ => StatusCodes.Status400BadRequest
            };
            return StatusCode(status, new UserProfileErrorResponse(exception.Message));
        }
    }
}

public sealed record SaveUserProfileRequest(
    [param: Required, StringLength(100, MinimumLength = 1)] string DisplayName,
    [param: Url, MaxLength(2048)] string? AvatarUrl,
    [param: Url, MaxLength(2048)] string? CoverUrl,
    [param: EnumDataType(typeof(Gender))] Gender Gender);

public sealed record UserProfileErrorResponse(string Message);
