using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Posts;
using Viora.Domain.Entities;

namespace viora_BE.Controllers;

[ApiController]
[Route("api/feed")]
[Authorize]
public sealed class FeedController(IMediator mediator) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [HttpGet]
    [ProducesResponseType<PostFeedResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PostFeedResponse>> ListCommunityPosts(
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 10,
        [FromQuery, MaxLength(255)] string? keyword = null,
        CancellationToken cancellationToken = default)
    {
        var response = await mediator.Send(
            new GetCommunityPostsQuery(page, pageSize, keyword, GetViewerUserId()),
            cancellationToken);

        return Ok(response);
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<CreatePostResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CreatePostResponse>> Create(
        [FromForm] CreatePostFormRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetViewerUserId(out var userId))
        {
            return Unauthorized();
        }

        var body = ParsePostBody(request);

        try
        {
            var files = request.Files?
                .Select(file => new CreatePostFile(
                    file.OpenReadStream(),
                    file.FileName,
                    file.ContentType,
                    file.Length))
                .ToList() ?? [];

            var response = await mediator.Send(new CreatePostCommand(
                userId,
                body.Content,
                body.Visibility,
                body.Latitude,
                body.Longitude,
                body.LocationName,
                body.Link,
                files), cancellationToken);

            return Created($"/api/feed/{response.Id}", response);
        }
        catch (FluentValidation.ValidationException exception)
        {
            return BadRequestProblem(
                "INVALID_POST",
                string.Join(" ", exception.Errors.Select(error => error.ErrorMessage)));
        }
        catch (CreatePostException exception)
        {
            return BadRequestProblem(exception.Code, exception.Message);
        }
    }

    private Guid? GetViewerUserId()
    {
        var value = User.FindFirstValue("user_id");
        return Guid.TryParse(value, out var userId) ? userId : null;
    }

    private bool TryGetViewerUserId(out Guid userId)
    {
        var value = User.FindFirstValue("user_id");
        return Guid.TryParse(value, out userId);
    }

    private static CreatePostBody ParsePostBody(CreatePostFormRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Post))
        {
            return new CreatePostBody(
                request.Content,
                request.Visibility,
                request.Latitude,
                request.Longitude,
                request.LocationName,
                request.Link);
        }

        try
        {
            return JsonSerializer.Deserialize<CreatePostBody>(request.Post, JsonOptions) ??
                new CreatePostBody(null, PostVisibility.Public, null, null, null, null);
        }
        catch (JsonException)
        {
            return new CreatePostBody(
                request.Post,
                PostVisibility.Public,
                null,
                null,
                null,
                null);
        }
    }

    private ObjectResult BadRequestProblem(string code, string detail)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Invalid post",
            Detail = detail
        };
        problem.Extensions["code"] = code;
        return new ObjectResult(problem) { StatusCode = StatusCodes.Status400BadRequest };
    }
}
