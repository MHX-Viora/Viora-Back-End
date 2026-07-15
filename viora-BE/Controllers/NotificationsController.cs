using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Notifications;
using Viora.Domain.Entities;

namespace viora_BE.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public sealed class NotificationsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<NotificationListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<NotificationListResponse>> List(
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 20,
        [FromQuery] bool? isRead = null,
        [FromQuery] NotificationType? type = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetViewerUserId(out var userId)) return Unauthorized();

        var response = await mediator.Send(
            new GetNotificationsQuery(userId, page, pageSize, isRead, type),
            cancellationToken);

        return Ok(response);
    }

    [HttpPut("{id:guid}/read")]
    [ProducesResponseType<MarkNotificationReadResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetViewerUserId(out var userId)) return Unauthorized();

        var result = await mediator.Send(
            new MarkNotificationReadCommand(userId, id),
            cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : NotFoundProblem(result.Message ?? "Khong tim thay thong bao.");
    }

    [HttpPut("read-all")]
    [ProducesResponseType<MarkAllNotificationsReadResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<MarkAllNotificationsReadResponse>> MarkAllRead(
        CancellationToken cancellationToken)
    {
        if (!TryGetViewerUserId(out var userId)) return Unauthorized();

        var response = await mediator.Send(
            new MarkAllNotificationsReadCommand(userId),
            cancellationToken);

        return Ok(response);
    }

    private bool TryGetViewerUserId(out Guid userId)
    {
        var value = User.FindFirstValue("user_id");
        return Guid.TryParse(value, out userId);
    }

    private ObjectResult NotFoundProblem(string detail)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = "Notification not found",
            Detail = detail
        };
        problem.Extensions["code"] = NotificationError.NotFound.ToString();
        return new ObjectResult(problem) { StatusCode = StatusCodes.Status404NotFound };
    }
}
