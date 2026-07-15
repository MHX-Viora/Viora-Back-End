using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Posts;

namespace viora_BE.Controllers;

[ApiController]
[Route("api/reels")]
[Authorize]
public sealed class ReelsController(IMediator mediator, ILogger<ReelsController> logger) : ControllerBase
{
    private const long ReelRequestLimit = CreateReelValidator.MaxVideoBytes + (128 * 1024);

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(ReelRequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = CreateReelValidator.MaxVideoBytes)]
    [ProducesResponseType<CreateReelResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Create(
        [FromForm] CreateReelFormRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetViewerUserId(out var userId))
        {
            return Unauthorized();
        }

        try
        {
            var video = request.Video is null
                ? null
                : new CreatePostFile(
                    request.Video.OpenReadStream(),
                    request.Video.FileName,
                    request.Video.ContentType,
                    request.Video.Length);

            logger.LogInformation(
                "Creating reel. UserId={UserId}, FileName={FileName}, ContentType={ContentType}, Length={Length}",
                userId,
                request.Video?.FileName,
                request.Video?.ContentType,
                request.Video?.Length);

            var response = await mediator.Send(new CreateReelCommand(
                userId,
                request.Content,
                request.Hashtags ?? [],
                video), cancellationToken);

            return Created($"/api/reels/{response.Id}", response);
        }
        catch (FluentValidation.ValidationException exception)
        {
            return BadRequestProblem(
                "INVALID_REEL",
                string.Join(" ", exception.Errors.Select(error => error.ErrorMessage)));
        }
        catch (CreatePostException exception)
        {
            return BadRequestProblem(exception.Code, exception.Message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unexpected reel upload failure. UserId={UserId}", userId);

            return ProblemResponse(
                StatusCodes.Status502BadGateway,
                "REEL_UPLOAD_FAILED",
                "Reel upload failed",
                "Khong the dang video. Vui long thu lai.");
        }
    }

    [HttpGet]
    [ProducesResponseType<VideoFeedResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> List(
        [FromQuery, Range(1, int.MaxValue)] int page,
        [FromQuery, Range(1, 100)] int pageSize,
        [FromQuery, Required] string sort,
        [FromQuery, MaxLength(255)] string? keyword,
        [FromQuery] Guid? userId,
        CancellationToken cancellationToken)
    {
        if (!TryGetViewerUserId(out var viewerUserId))
        {
            return Unauthorized();
        }

        var result = await mediator.Send(
            new GetShortVideosQuery(page, pageSize, sort, keyword, userId, viewerUserId),
            cancellationToken);

        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Invalid video feed request",
            Detail = result.Message
        };
        problem.Extensions["code"] = result.Error?.ToString();

        return new ObjectResult(problem) { StatusCode = StatusCodes.Status400BadRequest };
    }

    private bool TryGetViewerUserId(out Guid userId)
    {
        var value = User.FindFirstValue("user_id");
        return Guid.TryParse(value, out userId);
    }

    private ObjectResult BadRequestProblem(string code, string detail)
    {
        return ProblemResponse(StatusCodes.Status400BadRequest, code, "Invalid reel", detail);
    }

    private static ObjectResult ProblemResponse(int statusCode, string code, string title, string detail)
    {
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail
        };
        problem.Extensions["code"] = code;
        return new ObjectResult(problem) { StatusCode = statusCode };
    }
}

public sealed class CreateReelFormRequest
{
    [FromForm(Name = "content")]
    [MaxLength(5000)]
    public string? Content { get; init; }

    [FromForm(Name = "hashtags")]
    public List<string>? Hashtags { get; init; }

    [FromForm(Name = "video")]
    public IFormFile? Video { get; init; }
}
