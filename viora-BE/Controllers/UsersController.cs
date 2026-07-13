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
    private const long ProfileRequestLimit = ProfileImageValidator.MaxFileBytes * 2 + 128 * 1024;

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(ProfileRequestLimit)]
    [ProducesResponseType<UserResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<UserProfileErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<UserProfileErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<UserProfileErrorResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<UserProfileErrorResponse>(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<UserResponse>> Create(
        [FromForm] SaveUserProfileRequest request,
        CancellationToken cancellationToken)
    {
        var fileError = await ValidateImagesAsync(request.Avatar, request.Cover, cancellationToken);
        if (fileError is not null)
        {
            return BadRequest(new UserProfileErrorResponse(fileError));
        }

        if (!TryGetAccountId(out var accountId))
        {
            return Unauthorized(new UserProfileErrorResponse("Token đăng nhập không hợp lệ."));
        }

        try
        {
            using var avatarContent = request.Avatar?.OpenReadStream();
            using var coverContent = request.Cover?.OpenReadStream();
            var user = await userProfileService.CreateAsync(
                accountId,
                new SaveUserProfileCommand(
                    request.DisplayName,
                    request.Gender!.Value,
                    ToImage(request.Avatar, avatarContent),
                    ToImage(request.Cover, coverContent)),
                cancellationToken);
            return StatusCode(StatusCodes.Status201Created, user);
        }
        catch (UserProfileException exception)
        {
            return ProfileError(exception);
        }
        catch (ProfileImageStorageException)
        {
            return ImageStorageError();
        }
    }

    [HttpPatch]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(ProfileRequestLimit)]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<UserProfileErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<UserProfileErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<UserProfileErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<UserProfileErrorResponse>(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<UserResponse>> Update(
        [FromForm] UpdateUserProfileRequest request,
        CancellationToken cancellationToken)
    {
        var fileError = await ValidateImagesAsync(request.Avatar, request.Cover, cancellationToken);
        if (fileError is not null)
        {
            return BadRequest(new UserProfileErrorResponse(fileError));
        }

        if (!TryGetAccountId(out var accountId))
        {
            return Unauthorized(new UserProfileErrorResponse("Token đăng nhập không hợp lệ."));
        }

        try
        {
            using var avatarContent = request.Avatar?.OpenReadStream();
            using var coverContent = request.Cover?.OpenReadStream();
            var user = await userProfileService.UpdateAsync(
                accountId,
                new UpdateUserProfileCommand(
                    request.DisplayName,
                    request.Gender,
                    ToImage(request.Avatar, avatarContent),
                    ToImage(request.Cover, coverContent)),
                cancellationToken);
            return Ok(user);
        }
        catch (UserProfileException exception)
        {
            return ProfileError(exception);
        }
        catch (ProfileImageStorageException)
        {
            return ImageStorageError();
        }
    }

    private bool TryGetAccountId(out Guid accountId) =>
        Guid.TryParse(User.FindFirstValue("sub"), out accountId);

    private static ProfileImageFile? ToImage(IFormFile? file, Stream? content) =>
        file is null || content is null
            ? null
            : new ProfileImageFile(content, Path.GetFileName(file.FileName));

    private static async Task<string?> ValidateImagesAsync(
        IFormFile? avatar,
        IFormFile? cover,
        CancellationToken cancellationToken)
    {
        foreach (var file in new[] { avatar, cover })
        {
            if (file is null)
            {
                continue;
            }
            var validation = await ProfileImageValidator.ValidateAsync(file, cancellationToken);
            if (!validation.IsValid)
            {
                return validation.Error;
            }
        }
        return null;
    }

    private ActionResult<UserResponse> ProfileError(UserProfileException exception)
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

    private ActionResult<UserResponse> ImageStorageError() =>
        StatusCode(
            StatusCodes.Status502BadGateway,
            new UserProfileErrorResponse("Không thể tải ảnh lên. Vui lòng thử lại."));
}

public sealed record SaveUserProfileRequest(
    [param: Required, StringLength(100, MinimumLength = 1)] string DisplayName,
    [param: Required, EnumDataType(typeof(Gender))] Gender? Gender,
    IFormFile? Avatar,
    IFormFile? Cover);

public sealed record UpdateUserProfileRequest(
    [param: StringLength(100, MinimumLength = 1)] string? DisplayName,
    [param: EnumDataType(typeof(Gender))] Gender? Gender,
    IFormFile? Avatar,
    IFormFile? Cover);

public sealed record UserProfileErrorResponse(string Message);
