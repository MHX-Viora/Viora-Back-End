using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Legal;
using Viora.Domain.Entities;

namespace viora_BE.Controllers;

[ApiController]
[Route("api/legal")]
public sealed class LegalController(ILegalDocumentRepository repository) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LegalDocumentSummaryResponse>>> List(CancellationToken token) =>
        Ok(await repository.GetPublishedAsync(token));

    [HttpGet("{type}")]
    public async Task<ActionResult<LegalDocumentResponse>> Detail(LegalDocumentType type, CancellationToken token)
    {
        if (!Enum.IsDefined(type)) return BadRequest(new ProblemDetails { Title = "Invalid legal document type." });
        var document = await repository.GetPublishedAsync(type, token);
        return document is null ? NotFound() : Ok(document);
    }

    [Authorize]
    [HttpPost("accept")]
    public async Task<IActionResult> Accept(AcceptLegalDocumentRequest request, CancellationToken token)
    {
        if (!Guid.TryParse(User.FindFirstValue("user_id"), out var userId)) return Unauthorized();
        var result = await repository.AcceptAsync(
            userId, request.DocumentId, request.Version, request.AppVersion, request.DeviceType,
            HttpContext.Connection.RemoteIpAddress?.ToString(), token);
        return ToResult(result);
    }

    private IActionResult ToResult(LegalMutationResult result)
    {
        if (result.Success) return Ok(new { success = true });
        var status = result.Error switch
        {
            LegalMutationError.NotFound => StatusCodes.Status404NotFound,
            LegalMutationError.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };
        return Problem(statusCode: status, title: "Legal document request failed.", detail: result.Message);
    }
}

public sealed record AcceptLegalDocumentRequest(
    Guid DocumentId,
    [property: Required, MaxLength(20)] string Version,
    [property: MaxLength(30)] string? AppVersion,
    short? DeviceType);

