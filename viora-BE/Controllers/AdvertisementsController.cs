using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Advertisements;
using Viora.Domain.Entities;

namespace viora_BE.Controllers;

[ApiController]
[Authorize]
[Route("api/advertisements")]
public sealed class AdvertisementsController(IAdvertisementService advertisements, ILogger<AdvertisementsController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateAdvertisementBody body, CancellationToken cancellationToken) =>
        await ExecuteAsync(async userId =>
        {
            var result = await advertisements.CreateAsync(userId, new(
                body.PostId, body.Objective, body.DestinationUrl, body.CtaType, body.TargetingMode,
                body.MinimumAge, body.MaximumAge, body.TargetLocation, body.DailyBudget,
                body.TotalBudget, body.StartAt, body.EndAt), cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
        });

    [HttpPost("{id:guid}/submit")]
    public Task<IActionResult> Submit(Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync(async userId => Ok(await advertisements.SubmitAsync(userId, id, cancellationToken)));

    [HttpPost("{id:guid}/pause")]
    public Task<IActionResult> Pause(Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync(async userId => Ok(await advertisements.PauseAsync(userId, id, cancellationToken)));

    [HttpPost("{id:guid}/resume")]
    public Task<IActionResult> Resume(Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync(async userId => Ok(await advertisements.ResumeAsync(userId, id, cancellationToken)));

    [HttpPost("{id:guid}/cancel")]
    public Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync(async userId => Ok(await advertisements.CancelAsync(userId, id, cancellationToken)));

    [HttpGet("mine")]
    public Task<IActionResult> Mine(
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 20,
        [FromQuery] AdvertisementStatus? status = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async userId => Ok(await advertisements.GetMineAsync(userId, page, pageSize, status, cancellationToken)));

    [HttpGet("delivery")]
    public Task<IActionResult> Delivery(
        [FromQuery] AdvertisementPlacement placement,
        [FromQuery, Range(1, 20)] int take = 3,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async userId => Ok(await advertisements.GetDeliveryAsync(userId, placement, take, cancellationToken)));

    [HttpGet("{id:guid}")]
    public Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync(async userId => Ok(await advertisements.GetAsync(userId, id, cancellationToken)));

    [HttpPost("{id:guid}/impressions")]
    public Task<IActionResult> Impression(Guid id, AdvertisementEventBody body, CancellationToken cancellationToken) =>
        ExecuteAsync(async userId => Ok(await advertisements.RecordEventAsync(userId, id, AdvertisementEventType.Impression, new(body.ClientEventId), cancellationToken)));

    [HttpPost("{id:guid}/clicks")]
    public Task<IActionResult> Click(Guid id, AdvertisementEventBody body, CancellationToken cancellationToken) =>
        ExecuteAsync(async userId => Ok(await advertisements.RecordEventAsync(userId, id, AdvertisementEventType.Click, new(body.ClientEventId), cancellationToken)));

    [HttpPost("{id:guid}/feedback")]
    public Task<IActionResult> Feedback(Guid id, AdvertisementFeedbackBody body, CancellationToken cancellationToken) =>
        ExecuteAsync(async userId => Ok(await advertisements.RecordFeedbackAsync(userId, id, new(body.Type, body.Reason), cancellationToken)));

    private async Task<IActionResult> ExecuteAsync(Func<Guid, Task<IActionResult>> action)
    {
        if (!Guid.TryParse(User.FindFirstValue("user_id"), out var userId)) return Unauthorized();
        try { return await action(userId); }
        catch (AdvertisementNotFoundException exception) { return Error(404, exception.Code, exception.Message); }
        catch (AdvertisementForbiddenException exception) { return Error(403, "ADVERTISEMENT_FORBIDDEN", exception.Message); }
        catch (AdvertisementInsufficientBalanceException exception)
        {
            return Error(422, exception.Code, exception.Message, new { exception.Available, exception.Required, exception.Shortfall });
        }
        catch (AdvertisementValidationException exception) { return Error(422, exception.Code, exception.Message); }
        catch (AdvertisementConflictException exception) { return Error(409, exception.Code, exception.Message); }
        catch (Exception exception)
        {
            logger.LogError(exception, "Advertisement request failed. UserId: {UserId}, Path: {Path}", userId, Request.Path);
            return Error(500, "ADVERTISEMENT_ERROR", "Không thể thực hiện thao tác lúc này. Vui lòng thử lại.");
        }
    }

    private static ObjectResult Error(int status, string code, string message, object? details = null) =>
        new(new { error = new { code, message, details } }) { StatusCode = status };
}

public sealed record CreateAdvertisementBody(
    Guid PostId,
    AdvertisementObjective Objective,
    [property: StringLength(2048)] string? DestinationUrl,
    AdvertisementCtaType CtaType,
    AdvertisementTargetingMode TargetingMode,
    short? MinimumAge,
    short? MaximumAge,
    [property: StringLength(120)] string? TargetLocation,
    [property: Range(typeof(decimal), "50000", "1000000000")] decimal? DailyBudget,
    [property: Range(typeof(decimal), "50000", "1000000000")] decimal TotalBudget,
    DateTime StartAt,
    DateTime EndAt);

public sealed record AdvertisementEventBody([property: Required, StringLength(100, MinimumLength = 8)] string ClientEventId);
public sealed record AdvertisementFeedbackBody(AdvertisementFeedbackType Type, [property: StringLength(500)] string? Reason);
