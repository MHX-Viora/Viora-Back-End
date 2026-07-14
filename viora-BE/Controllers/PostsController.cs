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
[Route("api/posts")]
[Authorize]
public sealed class PostsController(IMediator mediator) : ControllerBase
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

            return Created($"/api/posts/{response.Id}", response);
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

    [HttpPost("{postId:guid}/reactions")]
    [ProducesResponseType<PostReactionResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> React(
        Guid postId,
        ReactionPostRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetViewerUserId(out var userId)) return Unauthorized();
        var result = await mediator.Send(new ReactPostCommand(userId, postId, request.ReactionType), cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{postId:guid}/comments")]
    [ProducesResponseType<CommentResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Comment(
        Guid postId,
        CommentPostRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetViewerUserId(out var userId)) return Unauthorized();
        var result = await mediator.Send(new CreateCommentCommand(userId, postId, request.Content), cancellationToken);
        return ToActionResult(result, StatusCodes.Status201Created);
    }

    [HttpPost("/api/comments/{commentId:guid}/replies")]
    [ProducesResponseType<CommentResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Reply(
        Guid commentId,
        ReplyCommentRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetViewerUserId(out var userId)) return Unauthorized();
        var result = await mediator.Send(new ReplyCommentCommand(userId, commentId, request.Content), cancellationToken);
        return ToActionResult(result, StatusCodes.Status201Created);
    }

    [HttpPost("{postId:guid}/save")]
    [ProducesResponseType<SavePostResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Save(Guid postId, CancellationToken cancellationToken)
    {
        if (!TryGetViewerUserId(out var userId)) return Unauthorized();
        var result = await mediator.Send(new ToggleSavePostCommand(userId, postId), cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{postId:guid}/share")]
    [ProducesResponseType<SharePostResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Share(
        Guid postId,
        SharePostRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetViewerUserId(out var userId)) return Unauthorized();
        var result = await mediator.Send(new SharePostCommand(userId, postId, request.Content), cancellationToken);
        return ToActionResult(result, StatusCodes.Status201Created);
    }

    [HttpDelete("{postId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid postId, CancellationToken cancellationToken)
    {
        if (!TryGetViewerUserId(out var userId)) return Unauthorized();
        var result = await mediator.Send(new DeletePostCommand(userId, postId), cancellationToken);
        return result.IsSuccess ? NoContent() : ToActionResult(result);
    }

    [HttpPost("{postId:guid}/report")]
    [ProducesResponseType<ReportPostResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Report(
        Guid postId,
        ReportPostRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetViewerUserId(out var userId)) return Unauthorized();
        var result = await mediator.Send(new ReportPostCommand(userId, postId, request.Reason, request.Description), cancellationToken);
        return ToActionResult(result, StatusCodes.Status201Created);
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

    private IActionResult ToActionResult<T>(Result<T> result, int successStatus = StatusCodes.Status200OK)
    {
        if (result.IsSuccess)
        {
            return successStatus == StatusCodes.Status201Created
                ? StatusCode(StatusCodes.Status201Created, result.Value)
                : Ok(result.Value);
        }

        var status = result.Error switch
        {
            PostInteractionError.NotFound => StatusCodes.Status404NotFound,
            PostInteractionError.Forbidden => StatusCodes.Status403Forbidden,
            PostInteractionError.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };

        var problem = new ProblemDetails
        {
            Status = status,
            Title = "Post interaction failed",
            Detail = result.Message
        };
        problem.Extensions["code"] = result.Error?.ToString();
        return new ObjectResult(problem) { StatusCode = status };
    }
}

public sealed record ReactionPostRequest(ReactionType ReactionType);
public sealed record CommentPostRequest([param: Required, MaxLength(5000)] string Content);
public sealed record ReplyCommentRequest([param: Required, MaxLength(5000)] string Content);
public sealed record SharePostRequest([param: MaxLength(5000)] string? Content);
public sealed record ReportPostRequest(ReportReason Reason, [param: MaxLength(1000)] string? Description);

public sealed class CreatePostFormRequest
{
    [FromForm(Name = "post")]
    public string? Post { get; init; }

    [FromForm(Name = "content")]
    [MaxLength(5000)]
    public string? Content { get; init; }

    [FromForm(Name = "visibility")]
    public PostVisibility Visibility { get; init; } = PostVisibility.Public;

    [FromForm(Name = "latitude")]
    public double? Latitude { get; init; }

    [FromForm(Name = "longitude")]
    public double? Longitude { get; init; }

    [FromForm(Name = "locationName")]
    [MaxLength(255)]
    public string? LocationName { get; init; }

    [FromForm(Name = "link")]
    [MaxLength(255)]
    public string? Link { get; init; }

    [FromForm(Name = "files")]
    public List<IFormFile>? Files { get; init; }
}

public sealed record CreatePostBody(
    string? Content,
    PostVisibility Visibility,
    double? Latitude,
    double? Longitude,
    string? LocationName,
    string? Link);
