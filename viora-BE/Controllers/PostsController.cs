using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Posts;
using Viora.Application.Sharing;
using Viora.Domain.Entities;

namespace viora_BE.Controllers;

[ApiController]
[Route("api/posts")]
[Authorize]
public sealed class PostsController(IMediator mediator, IShareLinkService shareLinkService) : ControllerBase
{
    [HttpGet("{postId:guid}")]
    [ProducesResponseType<PostDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Detail(Guid postId, CancellationToken cancellationToken)
    {
        if (!TryGetViewerUserId(out var userId)) return Unauthorized();
        var result = await mediator.Send(new GetPostDetailQuery(userId, postId), cancellationToken);
        return ToActionResult(result);
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

    [HttpGet("{postId:guid}/comments")]
    [ProducesResponseType<PostCommentsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListComments(
        Guid postId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? sort = "newest",
        CancellationToken cancellationToken = default)
    {
        if (!TryGetViewerUserId(out var userId)) return Unauthorized();
        var result = await mediator.Send(new GetPostCommentsQuery(userId, postId, page, pageSize, sort), cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("/api/comments/{commentId:guid}/replies")]
    [ProducesResponseType<CommentReplyListItemResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Reply(
        Guid commentId,
        ReplyCommentRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetViewerUserId(out var userId)) return Unauthorized();
        var result = await mediator.Send(new ReplyCommentCommand(userId, commentId, request.Content), cancellationToken);
        return ToActionResult(result, StatusCodes.Status201Created);
    }

    [HttpGet("/api/comments/{commentId:guid}/replies")]
    [ProducesResponseType<CommentRepliesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListReplies(
        Guid commentId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? sort = "oldest",
        CancellationToken cancellationToken = default)
    {
        if (!TryGetViewerUserId(out var userId)) return Unauthorized();
        var result = await mediator.Send(new GetCommentRepliesQuery(userId, commentId, page, pageSize, sort), cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("/api/comments/{commentId:guid}/like")]
    [ProducesResponseType<CommentLikeApiResponse<CommentLikeResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<CommentLikeApiResponse<object>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<CommentLikeApiResponse<object>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<CommentLikeApiResponse<object>>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CommentLikeApiResponse<CommentLikeResponse>>> ToggleCommentLike(
        Guid commentId,
        CancellationToken cancellationToken)
    {
        if (!TryGetViewerUserId(out var userId))
        {
            return Unauthorized(new CommentLikeApiResponse<object>(false, "Unauthorized.", null));
        }

        var result = await mediator.Send(new ToggleCommentLikeCommand(userId, commentId), cancellationToken);
        if (result.IsSuccess)
        {
            return Ok(new CommentLikeApiResponse<CommentLikeResponse>(true, "Thao tác thành công.", result.Value));
        }

        var message = result.Error == PostInteractionError.NotFound
            ? "Không tìm thấy bình luận."
            : "Không thể thích bình luận này.";
        var response = new CommentLikeApiResponse<object>(false, message, null);
        return result.Error == PostInteractionError.NotFound
            ? NotFound(response)
            : BadRequest(response);
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
        CancellationToken cancellationToken)
    {
        if (!TryGetViewerUserId(out var userId)) return Unauthorized();
        var result = await mediator.Send(new SharePostCommand(userId, postId), cancellationToken);
        return ToActionResult(result, StatusCodes.Status201Created);
    }

    [HttpGet("{postId:guid}/share")]
    [ProducesResponseType<ShareLinkResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetShareLink(Guid postId, CancellationToken cancellationToken)
    {
        if (!TryGetViewerUserId(out var userId)) return Unauthorized();
        return ToShareActionResult(await shareLinkService.GetPostShareLinkAsync(userId, postId, cancellationToken));
    }

    [HttpDelete("{postId:guid}")]
    [ProducesResponseType<DeletePostResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<MessageResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<MessageResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<MessageResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid postId, CancellationToken cancellationToken)
    {
        if (!TryGetViewerUserId(out var userId)) return Unauthorized();
        var result = await mediator.Send(new DeletePostCommand(userId, postId), cancellationToken);
        return result.IsSuccess
            ? Ok(new DeletePostResponse("Xóa bài viết thành công."))
            : ToMessageActionResult(result);
    }

    [HttpPost("{postId:guid}/report")]
    [ProducesResponseType<ReportPostResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<MessageResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<MessageResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<MessageResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<MessageResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Report(
        Guid postId,
        ReportPostRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetViewerUserId(out var userId)) return Unauthorized();
        var result = await mediator.Send(new ReportPostCommand(userId, postId, request.Reason, request.Description), cancellationToken);
        return ToMessageActionResult(result, StatusCodes.Status201Created);
    }

    private bool TryGetViewerUserId(out Guid userId)
    {
        var value = User.FindFirstValue("user_id");
        return Guid.TryParse(value, out userId);
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

    private IActionResult ToMessageActionResult<T>(Result<T> result, int successStatus = StatusCodes.Status200OK)
    {
        if (result.IsSuccess)
        {
            return StatusCode(successStatus, result.Value);
        }

        var status = result.Error switch
        {
            PostInteractionError.NotFound => StatusCodes.Status404NotFound,
            PostInteractionError.Forbidden => StatusCodes.Status403Forbidden,
            PostInteractionError.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };

        return StatusCode(status, new MessageResponse(result.Message ?? "Thao tác thất bại."));
    }

    private IActionResult ToShareActionResult<T>(ShareLinkResult<T> result)
    {
        if (result.IsSuccess) return Ok(result.Value);
        var status = result.Error == ShareLinkError.Forbidden ? StatusCodes.Status403Forbidden : StatusCodes.Status404NotFound;
        var problem = new ProblemDetails { Status = status, Title = "Share link request failed", Detail = result.Message };
        problem.Extensions["code"] = result.Error?.ToString();
        return new ObjectResult(problem) { StatusCode = status };
    }
}

public sealed record ReactionPostRequest(ReactionType ReactionType);
public sealed record CommentPostRequest([param: Required, MaxLength(5000)] string Content);
public sealed record ReplyCommentRequest([param: Required, MaxLength(5000)] string Content);
public sealed record ReportPostRequest(ReportReason Reason, [param: MaxLength(1000)] string? Description);
public sealed record MessageResponse(string Message);
public sealed record CommentLikeApiResponse<T>(bool Success, string Message, T? Data);

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
