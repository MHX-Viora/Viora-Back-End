using System.Security.Claims;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Articles;
using Viora.Application.Posts;
using Viora.Domain.Entities;

namespace viora_BE.Controllers;

[ApiController]
[Route("api/articles")]
[Authorize]
public sealed class ArticlesController(IMediator mediator, IMediaStorage mediaStorage) : ControllerBase
{
    [HttpPost("media")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(100_000_000)]
    [ProducesResponseType<IReadOnlyList<ArticleMediaUploadResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadMedia([FromForm] ArticleMediaUploadRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        if (request.Files.Count == 0) return ProblemResult(400, "MEDIA_REQUIRED", "Vui lòng chọn ít nhất một tệp.");
        if (request.Files.Count > 10) return ProblemResult(400, "MEDIA_LIMIT_EXCEEDED", "Chỉ được tải tối đa 10 tệp mỗi lần.");

        try
        {
            var uploaded = new List<ArticleMediaUploadResponse>(request.Files.Count);
            foreach (var file in request.Files)
            {
                var isImage = file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
                var isVideo = file.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase);
                if (!isImage && !isVideo)
                    return ProblemResult(400, "MEDIA_TYPE_INVALID", "Article chỉ hỗ trợ ảnh và video.");
                var maxBytes = isVideo ? 100_000_000L : 15_000_000L;
                if (file.Length <= 0 || file.Length > maxBytes)
                    return ProblemResult(400, "MEDIA_SIZE_INVALID", isVideo ? "Video tối đa 100 MB." : "Ảnh tối đa 15 MB.");

                await using var stream = file.OpenReadStream();
                var input = new CreatePostFile(stream, file.FileName, file.ContentType, file.Length);
                var result = isImage
                    ? await mediaStorage.UploadPostImageAsync(userId, input, cancellationToken)
                    : await mediaStorage.UploadReelVideoAsync(userId, input, cancellationToken);
                uploaded.Add(new ArticleMediaUploadResponse(result.MediaUrl, result.ThumbnailUrl, isImage ? ArticleBlockType.Image : ArticleBlockType.Video));
            }
            return Ok(uploaded);
        }
        catch (CreatePostException exception)
        {
            return ProblemResult(400, exception.Code, exception.Message);
        }
    }

    [HttpPost]
    [ProducesResponseType<ArticleResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Create(CreateArticleRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        try
        {
            var response = await mediator.Send(new CreateArticleCommand(
                userId, request.Title, request.Visibility, request.Blocks), cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = response.Id }, response);
        }
        catch (ValidationException exception)
        {
            return ProblemResult(StatusCodes.Status400BadRequest, "ARTICLE_INVALID",
                string.Join(" ", exception.Errors.Select(x => x.ErrorMessage)));
        }
        catch (CreatePostException exception)
        {
            return ProblemResult(StatusCodes.Status400BadRequest, exception.Code, exception.Message);
        }
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<ArticleResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, UpdateArticleRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        return ToResult(await mediator.Send(new UpdateArticleCommand(
            userId, id, request.Title, request.Visibility, request.Blocks), cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ArticleResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        return ToResult(await mediator.Send(new GetArticleQuery(userId, id), cancellationToken));
    }

    private bool TryGetUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue("user_id"), out userId);

    private IActionResult ToResult(Result<ArticleResponse> result)
    {
        if (result.IsSuccess) return Ok(result.Value);
        var status = result.Error switch
        {
            PostInteractionError.Forbidden => StatusCodes.Status403Forbidden,
            PostInteractionError.NotFound => StatusCodes.Status404NotFound,
            PostInteractionError.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };
        return ProblemResult(status, result.Error?.ToString().ToUpperInvariant() ?? "ARTICLE_ERROR", result.Message ?? "Article error.");
    }

    private ObjectResult ProblemResult(int status, string code, string detail)
    {
        var problem = new ProblemDetails { Status = status, Title = "Article request failed", Detail = detail };
        problem.Extensions["code"] = code;
        return new ObjectResult(problem) { StatusCode = status };
    }
}

public sealed record CreateArticleRequest(
    string Title,
    PostVisibility Visibility,
    IReadOnlyList<CreateArticleBlockRequest> Blocks);

public sealed record UpdateArticleRequest(
    string Title,
    PostVisibility Visibility,
    IReadOnlyList<UpdateArticleBlockRequest> Blocks);

public sealed class ArticleMediaUploadRequest
{
    public List<IFormFile> Files { get; init; } = [];
}

public sealed record ArticleMediaUploadResponse(
    string MediaUrl,
    string? ThumbnailUrl,
    ArticleBlockType Type);
