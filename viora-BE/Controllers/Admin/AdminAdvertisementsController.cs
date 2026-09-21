using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Advertisements;
using Viora.Domain.Entities;

namespace viora_BE.Controllers.Admin;

[ApiController]
[Authorize(Roles = "2")]
[Route("api/admin/advertisements")]
public sealed class AdminAdvertisementsController(IAdvertisementService advertisements) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AdvertisementPage>> List(
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 20,
        [FromQuery] AdvertisementStatus? status = null,
        CancellationToken cancellationToken = default) =>
        Ok(await advertisements.GetAdminPageAsync(page, pageSize, status, cancellationToken));

    [HttpPost("{id:guid}/approve")]
    public Task<ActionResult<AdvertisementResponse>> Approve(Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync(adminId => advertisements.ApproveAsync(adminId, id, cancellationToken));

    [HttpPost("{id:guid}/reject")]
    public Task<ActionResult<AdvertisementResponse>> Reject(Guid id, RejectAdvertisementBody body, CancellationToken cancellationToken) =>
        ExecuteAsync(adminId => advertisements.RejectAsync(adminId, id, body.Reason, cancellationToken));

    private async Task<ActionResult<AdvertisementResponse>> ExecuteAsync(Func<Guid, Task<AdvertisementResponse>> action)
    {
        if (!Guid.TryParse(User.FindFirstValue("user_id"), out var adminId)) return Unauthorized();
        try { return Ok(await action(adminId)); }
        catch (AdvertisementNotFoundException) { return Error(404, "ADVERTISEMENT_NOT_FOUND", "Không tìm thấy quảng cáo."); }
        catch (AdvertisementValidationException exception) { return Error(422, exception.Code, exception.Message); }
        catch (AdvertisementConflictException exception) { return Error(409, exception.Code, exception.Message); }
    }

    private static ObjectResult Error(int status, string code, string message) =>
        new(new { error = new { code, message } }) { StatusCode = status };
}

public sealed record RejectAdvertisementBody([property: Required, StringLength(500, MinimumLength = 3)] string Reason);
